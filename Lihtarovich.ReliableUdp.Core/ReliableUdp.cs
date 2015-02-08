// ******************************************************************************
// Module:  ReliableUDP.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System;
using System.Net;
using System.Net.Sockets;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Implements the lightweight Reliable UDP protocol interface
  /// </summary>
  public sealed class ReliableUdp : IDisposable
  {
    private readonly ReliableUdpConnectionControlBlock m_tcb;

    /// <summary>
    /// Local Endpoint
    /// </summary>
    public IPEndPoint LocalEndpoint
    {
      get { return m_tcb.LocalEndpoint; }
    }

    /// <summary>
    /// Gets size of transmission packet
    /// </summary>
    public int CurrentSizeOfPacket
    {
      get { return m_tcb.DefaultMaxPacketSize; }
    }

    /// <summary>
    /// Gets size of transmitting window
    /// </summary>
    public int TransmittingWindowSize
    {
      get { return m_tcb.TransmittingWindowSize; }
    }

    /// <summary>
    /// Initializes a new instance of the ReliableUdp 
    /// </summary>
    public ReliableUdp()
    {
      m_tcb = new ReliableUdpConnectionControlBlock();
    }

    /// <summary>
    /// Initializes a new instance of the ReliableUdp 
    /// that listens for incoming messages 
    /// on the specified local IP address and port number.
    /// </summary>
    /// <param name="localAddress">An <see cref="IPAddress"/> that represents the local IP address.</param>
    /// <param name="port">The port on which to listen for incoming messages. Set 0 to get dynamically assigned port</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="System.Security.SecurityException"></exception>  
    public ReliableUdp(IPAddress localAddress, int port = 0)
    {
      if (port < 0)
      {
        throw new ArgumentOutOfRangeException("port", "port must be non-negative");
      }
      m_tcb = new ReliableUdpConnectionControlBlock();
      StartListener(new IPEndPoint(localAddress, port));
    }

    /// <summary>
    /// Initializes a new instance of the ReliableUdp 
    /// that listens for incoming messages 
    /// on the specified local endpoint.
    /// </summary>
    /// <param name="localEndPoint">An IPEndPoint that represents the local endpoint to which to bind the listener</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="System.Security.SecurityException"></exception>  
    public ReliableUdp(IPEndPoint localEndPoint)
    {
      m_tcb = new ReliableUdpConnectionControlBlock();
      StartListener(localEndPoint);
    }

    /// <summary>
    /// Tunes the ReliableUdp inner settings such as approved size of packet and size of transmitting window.
    /// </summary>
    /// <param name="sizeOfPacket">Size of transmission packet.</param>
    /// <param name="transmittingWindowSize">Size of transmitting window</param>
    /// <param name="shortTimerPeriod">Timer interval before retransmission request</param>
    /// <param name="longTimerPeriod">Timer interval before release of resources in case of probable interrupted transmitting</param>
    /// <returns><c>true</c> if settings have successfully applied. <c>false</c> returns because of listener is already running</returns>
    /// <remarks>Default size of packet is 1456, which means that on most widly-spread Ethernet and Wi-Fi
    /// adapters it will use maximum packet size without ip fragmentation. ReliableUdp doesn't use
    /// MTU path technologies to find the best packet size between peers. If you have troubles with
    /// IP fragmentation in your network, decrease the sizeOfPacket on all estimated peers. 
    /// </remarks>
    /// <exception cref="ReliableUdpConfigurationException"><paramref name="sizeOfPacket"/> must be between 496 and 1456 bytes.
    /// Or <paramref name="transmittingWindowSize"/>is less than 1.
    /// Or timer interval settings is incorrect</exception>
    public bool InitSettings( int sizeOfPacket, int transmittingWindowSize, int shortTimerPeriod = 5800, int longTimerPeriod = 30000 )
    {
      return m_tcb.InitSettings(sizeOfPacket, transmittingWindowSize, shortTimerPeriod, longTimerPeriod);
    }

    /// <summary>
    /// Starts listening for incoming messages.
    /// </summary>
    /// <param name="localAddress">An <see cref="IPAddress"/> that represents the local IP address.</param>
    /// <param name="port">The port on which to listen for incoming messages. Set 0 to get dynamically assigned port</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="System.Security.SecurityException"></exception>  
    public void StartListener(IPAddress localAddress, int port = 0)
    {
      if (port < 0)
      {
        throw new ArgumentOutOfRangeException("port", "port must be non-negative");
      }
      StartListener(new IPEndPoint(localAddress, port));
    }

    /// <summary>
    /// Starts listening for incoming messages.
    /// </summary>
    /// <param name="localEndPoint">An IPEndPoint that represents the local endpoint to which to bind the listener</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="System.Security.SecurityException"></exception>  
    public void StartListener(IPEndPoint localEndPoint)
    {
      m_tcb.StartListener(localEndPoint);
    }

    /// <summary>
    /// Subscribes for reception of <see cref="ReliableUdpMessage"/> messages
    /// </summary>
    /// <param name="callback">Сallback method to receive messages</param>
    /// <param name="messageType">Message type to receive. Use ReliableUdpMessageTypes.Any
    ///  to receive all messages that comes from specified ip endpoint </param>
    /// <param name="ipEndPoint">Defines from what IP address and port to receive messages. 
    /// Set IPEndPoint.Port property in 0 for obtaining messages from any port of the specified address.
    /// Set <c>null</c> to receive messages from any ip endpoint </param>
    /// <returns><see cref="ReliableUdpSubscribeObject"/>. Represents a subscriber.</returns>
    public ReliableUdpSubscribeObject SubscribeOnMessages(ReliableUdpMessageCallback callback,
                                                          ReliableUdpMessageTypes messageType =
                                                            ReliableUdpMessageTypes.Any, IPEndPoint ipEndPoint = null)
    {
      return m_tcb.SubscribeOnMessages(callback, messageType, ipEndPoint);
    }

    /// <summary>
    /// Unsubscribes from reception of messages
    /// </summary>
    /// <param name="callback">Callback method that is used to subscribe to messages.
    /// If the same method was used for a set of subscriptions, all its subscriptions will be removed </param>
    public void Unsubscribe(ReliableUdpMessageCallback callback)
    {
      m_tcb.Unsubscribe(callback);
    }

    /// <summary>
    /// Unsubscribes from reception of messages using specified <see cref="ReliableUdpSubscribeObject"/>
    /// </summary>
    /// <param name="subscribeObject"></param>
    public void Unsubscribe(ReliableUdpSubscribeObject subscribeObject)
    {
      m_tcb.Unsubscribe(subscribeObject);
    }

    /// <summary>
    /// Sends the <paramref name="reliableUdpMessage"/> asynchronously to the specified endpoint.
    /// </summary>
    /// <param name="reliableUdpMessage"><see cref="ReliableUdpMessage"/> message to send.</param>
    /// <param name="remoteEndPoint">Ip endpoint of recipient of the message.</param>
    /// <param name="asyncCallback">The <see cref="AsyncCallback"/> delegate.</param>
    /// <param name="state">An object that contains state information for this request.</param>
    /// <returns>An IAsyncResult that references the asynchronous send.</returns>
    public IAsyncResult BeginSendMessage(ReliableUdpMessage reliableUdpMessage, IPEndPoint remoteEndPoint,
                                         AsyncCallback asyncCallback, Object state)
    {
      return m_tcb.BeginSend(reliableUdpMessage, remoteEndPoint, asyncCallback, state);
    }

    /// <summary>
    /// Ends a pending asynchronous send.
    /// </summary>
    /// <param name="asyncResult">An IAsyncResult that stores state information for this asynchronous operation. 
    /// </param>
    /// <returns><c>true</c> if the message was successfully sent. 
    /// <c>false</c> if sending the message was interrupted on a timeout.
    /// if <see cref="ReliableUdpMessage.NoAsk"/> is set to <c>true</c> the message can be delivered 
    /// damaged even if returned result equals <c>true</c></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ReliableUdpConfigurationException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="SocketException"></exception>
    public bool EndSendMessage(IAsyncResult asyncResult)
    {
      return m_tcb.EndSend(asyncResult);
    }

    /// <summary>
    /// Sends the <paramref name="reliableUdpMessage"/> pseudo-synchronously to the specified endpoint.
    /// </summary>
    /// <remarks>Asynchronous methods of sending and receiving packets are actually used.
    /// But initial start of sending is carried out in the current thread.
    /// If <see cref="ReliableUdpMessage.NoAsk"/> is set to <c>true</c> than method returns only when
    /// all packets will be sent.</remarks>
    /// <param name="reliableUdpMessage"><see cref="ReliableUdpMessage"/> message to send.</param>
    /// <param name="remoteEndPoint">Ip endpoint of recipient of the message.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ReliableUdpConfigurationException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="SocketException"></exception>
    public void SendMessage(ReliableUdpMessage reliableUdpMessage, IPEndPoint remoteEndPoint)
    {
      m_tcb.SendMessage(reliableUdpMessage, remoteEndPoint);
    }

    /// <summary>
    /// Closes the all connections and releases all associated resources.
    /// </summary>
    public void Dispose()
    {
      m_tcb.Dispose();
    }
  }
}