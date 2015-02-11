// ******************************************************************************
// Module:  ReliableUdpConnectionControlBlock.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;


namespace Lihtarovich.ReliableUdp.Core
{
  //Here is my own version of Reliable UDP. I try to made it lightweight, fast and simple.
  /// <summary>
  /// Implements the lightweight Reliable UDP protocol. Provides low-level network communication
  /// and hosts control structures.
  /// </summary>
  internal class ReliableUdpConnectionControlBlock : IDisposable
  {
    #region Dictionaries

    /// <summary>
    /// Gets byte[] array for specified key pair. It is used for assembling incoming messages.
    /// </summary>
    public ConcurrentDictionary<Tuple<EndPoint, Int32>, byte[]> IncomingStreams { get; private set; }

    /// <summary>
    /// Gets byte[] array for specified key pair. It is used for sending outcoming messages.
    /// </summary>
    public ConcurrentDictionary<Tuple<EndPoint, Int32>, byte[]> OutcomingStreams { get; private set; }

    /// <summary>
    /// Gets connection record for specified key pair.
    /// </summary>
    private readonly ConcurrentDictionary<Tuple<EndPoint, Int32>, ReliableUdpConnectionRecord> m_listOfHandlers;

    /// <summary>
    /// Gets list of message subscribers.
    /// </summary>
    private readonly List<ReliableUdpSubscribeObject> m_subscribers;

    #endregion

    #region Network
    /// <summary>
    /// Underlying network Socket.
    /// </summary>
    private Socket m_socketIn;

    /// <summary>
    /// Port for incoming connections
    /// </summary>
    private int m_port;

    /// <summary>
    /// Local IP address
    /// </summary>
    private IPAddress m_ipAddress;

    /// <summary>
    /// Local endpoint
    /// </summary>
    public IPEndPoint LocalEndpoint { get; private set; }

    #endregion

    #region Other variables
    /// <summary>
    /// This variable is used as boolean. Indicates whether the listner is started
    /// </summary>
    private int m_isListenerStarted;

    /// <summary>
    /// Locker object for multi-threading coordination
    /// </summary>
    private readonly object m_locker = new object();

    /// <summary>
    /// Gets state collection of finite state automaton
    /// </summary>
    public StatesCollection States { get; private set; }

    /// <summary>
    /// Cryptographic random generator. It is used for creating <see cref="ReliableUdpConnectionRecord.TransmissionId"/>
    /// </summary>
    private readonly RNGCryptoServiceProvider m_randomCrypto;
    #endregion

    #region Protocol settings
    /// <summary>
    /// Approved size of transmission packet. 
    /// </summary>
    private int m_defaultMaxPacketSize = 1456;

    /// <summary>
    /// Gets or sets approved size of transmission packet. 
    /// </summary>
    /// <remarks>Default size of packet is 1456, which means that on most widely-spread Ethernet and Wi-Fi
    /// adapters it will use maximum packet size without ip fragmentation
    /// </remarks>
    internal int DefaultMaxPacketSize
    {
      get { return m_defaultMaxPacketSize; }
      private set
      {
        //EN: Min MTU for IP networks - Ip.Header(without options) - Udp.Header - ReliableUdpHeader
        if (value < 532)
        {
          throw new ReliableUdpConfigurationException("very small udp packet");
        }
        //EN: Maximum packet size in Ethernet - Ip.Header(without options) - Udp.Header - ReliableUdpHeader
        if (value > 1456)
        {
          throw new ReliableUdpConfigurationException("size of udp payload is too big to pass through Ethernet ");
        }
        m_defaultMaxPacketSize = value;
      }
    }
    /// <summary>
    /// Size of transmitting window
    /// </summary>
    private int m_windowsSize = 8;

    /// <summary>
    /// Gets or sets size of transmitting window
    /// </summary>
    internal int TransmittingWindowSize
    {
      get { return m_windowsSize; }
      private set
      {
        if ( value < 1 )
        {
          throw new ReliableUdpConfigurationException( "Transmission window size must be greater than 0." );
        }
        m_windowsSize = value;
      }
    }

    
    private int m_shortTimerPeriod = 5800;
    /// <summary>
    /// Timer interval before retransmission request
    /// </summary>
    internal int ShortTimerPeriod
    {
      get { return m_shortTimerPeriod; }
      private set
      {
        if (value < 2000)
        {
          throw new ReliableUdpConfigurationException("Minimal short timer period is 2 seconds");
        }
        if (value > LongTimerPeriod)
        {
          throw new ReliableUdpConfigurationException("Short timer period can not be greater than Long timer period");
        }
        m_shortTimerPeriod = value;
      }
    }

    private int m_longTimerPeriod = 30000;
    /// <summary>
    /// Timer interval before release of resources in case of probable interrupted transmitting 
    /// </summary>
    internal int LongTimerPeriod
    {
      get { return m_longTimerPeriod; }
      private set
      {
        if ( value < ShortTimerPeriod )
        {
          throw new ReliableUdpConfigurationException( "Long timer can not be less than Short timer period" );
        }
        if (value<5800)
        {
          throw new ReliableUdpConfigurationException("Minimal long timer period is 5800 msec");
        }
        //EN: this is 2MSL default windows value
        if ( value > 240000 )
        {
          throw new ReliableUdpConfigurationException("Maximum long timer period can not be greater than 2MSL ( 240 sec )");
        }
        m_longTimerPeriod = value;
      }
    }

    #endregion

    #region Ctors and initialization of settings
    /// <summary>
    /// Initializes a new instance of the ReliableUdpConnectionControlBlock 
    /// with specified size of transmission packet
    /// </summary>
    /// <exception cref="System.Net.Sockets.SocketException">An error occurred when attempting to access the socket. </exception>
    public ReliableUdpConnectionControlBlock()
    {
      IncomingStreams = new ConcurrentDictionary<Tuple<EndPoint, Int32>, byte[]>();
      OutcomingStreams = new ConcurrentDictionary<Tuple<EndPoint, Int32>, byte[]>();


      m_listOfHandlers = new ConcurrentDictionary<Tuple<EndPoint, Int32>, ReliableUdpConnectionRecord>();
      m_subscribers = new List<ReliableUdpSubscribeObject>();

      States = new StatesCollection();

      m_randomCrypto = new RNGCryptoServiceProvider();

      this.m_socketIn = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
      this.m_socketIn.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    }

    /// <summary>
    /// Tunes the ReliableUdp inner settings such as approved size of packet and size of transmitting window
    /// </summary>
    /// <param name="sizeOfPacket">Approved size of transmission packet.</param>
    /// <param name="transmittingWindowSize">Size of transmitting window</param>
    /// <param name="shortTimerPeriod">Timer interval before retransmission request</param>
    /// <param name="longTimerPeriod">Timer interval before release of resources in case of probable interrupted transmitting</param>
    /// <returns><c>true</c> if settings have successfully applied</returns>
    /// <exception cref="ReliableUdpConfigurationException"><paramref name="sizeOfPacket"/> must be between 496 and 1456 bytes.
    /// Or <paramref name="transmittingWindowSize"/>is less than 1.
    /// Or timer interval settings is incorrect</exception>
    public bool InitSettings(int sizeOfPacket, int transmittingWindowSize, int shortTimerPeriod , int longTimerPeriod )
    {
      try
      {
        System.Threading.Monitor.Enter(m_locker);
        if (m_isListenerStarted != 0)
        {
          return false;
        }
        DefaultMaxPacketSize = sizeOfPacket;
        TransmittingWindowSize = transmittingWindowSize;
        ShortTimerPeriod = shortTimerPeriod;
        LongTimerPeriod = longTimerPeriod;
        return true;
      }
      finally
      {
        System.Threading.Monitor.Exit( m_locker );
      }
    }

    #endregion

    #region Subscription on messages
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
    internal ReliableUdpSubscribeObject SubscribeOnMessages(ReliableUdpMessageCallback callback,
                                                            ReliableUdpMessageTypes messageType, IPEndPoint ipEndPoint)
    {
      try
      {
        System.Threading.Monitor.Enter(m_locker);
        ReliableUdpSubscribeObject subscribe = new ReliableUdpSubscribeObject(callback, messageType, ipEndPoint);
        if (m_subscribers.Contains(subscribe))
        {
          return m_subscribers[m_subscribers.IndexOf(subscribe)];
        }
        m_subscribers.Add(subscribe);
        return subscribe;
      }
      finally
      {
        System.Threading.Monitor.Exit(m_locker);
      }
    }

    /// <summary>
    /// Unsubscribes from reception of messages
    /// </summary>
    /// <param name="callback">Callback method that is used to subscribe to messages.
    /// If the same method was used for a set of subscriptions, all its subscriptions will be removed </param>
    internal void Unsubscribe(ReliableUdpMessageCallback callback)
    {
      try
      {
        System.Threading.Monitor.Enter(m_locker);
        foreach (ReliableUdpSubscribeObject subscriber in m_subscribers.ToArray())
        {
          if (subscriber.ReliableUdpMessageCallback == callback)
          {
            m_subscribers.Remove(subscriber);
          }
        }
      }
      finally
      {
        System.Threading.Monitor.Exit(m_locker);
      }
    }

    /// <summary>
    /// Unsubscribes from reception of messages using specified <see cref="ReliableUdpSubscribeObject"/>
    /// </summary>
    /// <param name="subscribeObject"></param>
    internal void Unsubscribe(ReliableUdpSubscribeObject subscribeObject)
    {
      try
      {
        System.Threading.Monitor.Enter(m_locker);
        m_subscribers.Remove(subscribeObject);
      }
      finally
      {
        System.Threading.Monitor.Exit(m_locker);
      }
    }

    /// <summary>
    /// Passes message to subscribers
    /// </summary>
    /// <param name="reliableUdpMessage"><see cref="ReliableUdpMessage"/></param>
    /// <param name="remoteClient">Endpoint of sender</param>
    internal void PassMessageToSubscribers( ReliableUdpMessage reliableUdpMessage, IPEndPoint remoteClient )
    {

      try
      {
        //EN: To protect from calling removed callback delegate
        System.Threading.Monitor.Enter( m_locker );
        foreach ( ReliableUdpSubscribeObject subscriber in m_subscribers )
        {
          bool isRequiredEp = (subscriber.IpEndPoint == null)
                              || ((subscriber.IpEndPoint.Port == 0)
                                  && subscriber.IpEndPoint.Address.Equals( remoteClient.Address ))
                              || (subscriber.IpEndPoint.Equals( remoteClient ));
          bool isRequiredMessageType = (subscriber.ReliableUdpMessageType == ReliableUdpMessageTypes.Any)
                                       || (subscriber.ReliableUdpMessageType == reliableUdpMessage.Type);
          if ( isRequiredEp && isRequiredMessageType )
          {
            if ( subscriber.ReliableUdpMessageCallback != null )
              subscriber.ReliableUdpMessageCallback( reliableUdpMessage, remoteClient );
          }
        }
      }
      finally
      {
        System.Threading.Monitor.Exit( m_locker );
      }
    }
    #endregion

    #region Receiving

    /// <summary>
    /// Starts asynchronous udp server to listen on the specified port and address.
    /// To receive messages use <see cref="SubscribeOnMessages"/> method.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="System.Security.SecurityException"></exception>   
    public void StartListener(IPEndPoint localEndPoint)
    {
      try
      {
        System.Threading.Monitor.Enter(m_locker);
        if (m_isListenerStarted != 0)
          return;
        if ( localEndPoint == null )
        {
          throw new ArgumentNullException( "localEndPoint" );
        }
        this.LocalEndpoint = localEndPoint;
        this.m_ipAddress = this.LocalEndpoint.Address;
        this.m_port = this.LocalEndpoint.Port;

        this.m_socketIn.Bind(LocalEndpoint);
        Interlocked.Increment(ref m_isListenerStarted);
        Debug.WriteLine("Starting Listener on {0}:{1}", this.m_ipAddress, this.m_port);
        Receive();
      }
      catch (SocketException ex)
      {
        if (ex.ErrorCode == 10048)
          throw new ReliableUdpConfigurationException("Address already in use. Use another port", ex);
        throw;
      }
      finally
      {
        System.Threading.Monitor.Exit(m_locker);
      }
    }

    /// <summary>
    /// Start async I/O cicle to receive packets
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="System.Security.SecurityException"></exception>   
    private void Receive()
    {
      bool hasError = true;
      try
      {
        EndPoint connectedClient = new IPEndPoint(IPAddress.Any, 0);
        //RU: создаем новый буфер, для каждого socket.BeginReceiveFrom (? как отработает GC??)      
        //EN: create new buffer for each socket.BeginReceiveFrom (? how GC will work on it ?)
        byte[] buffer = new byte[DefaultMaxPacketSize + ReliableUdpHeader.Length];
        //RU: передаем буфер в качестве параметра для асинхронного метода
        //EN: pass buffer as state parameter
        this.m_socketIn.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref connectedClient, EndReceive,
                                         buffer);
        hasError = false;
      }
      finally
      {
        if (hasError)
        {
          this.Dispose();
        }
      }
    }

    /// <summary>
    /// Handles current async responce. Starts processing of udp payload in finite state automaton
    /// </summary>
    /// <param name="ar">An IAsyncResult that stores state information for this asynchronous operation.</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <exception cref="ReliableUdpConfigurationException"></exception>
    private void EndReceive(IAsyncResult ar)
    {
      bool hasError = true;
      try
      {
        EndPoint connectedClient = new IPEndPoint(IPAddress.Any, 0);
        int bytesRead = this.m_socketIn.EndReceiveFrom(ar, ref connectedClient);
        
        //RU: клиент присоединен, готовы принимать следующего
        //EN: client is connected. Start next receive
        Receive();
        //RU: т.к. простейший способ решить вопрос с буфером - получить ссылку на него 
        //RU: из IAsyncResult.AsyncState
        //EN: The simplest way to deal with buffer is to get it from IAsyncResult.AsyncState
        byte[] bytes = ((byte[]) ar.AsyncState).Slice(0, bytesRead);

        //RU: получаем заголовок пакета        
        //EN: getting header
        ReliableUdpHeader header;
        if (!ReliableUdpStateTools.ReadReliableUdpHeader(bytes, out header))
        {
          //throw new ArgumentException("bad packet header");
          //RU: не генерируем исключение, а просто отбрасываем пакет
          //EN: do not throw an exception, just drop this packet
          hasError = false;
          return;
        }

        Tuple<EndPoint, Int32> key = new Tuple<EndPoint, Int32>(connectedClient, header.TransmissionId);
        ReliableUdpConnectionRecord record = m_listOfHandlers.GetOrAdd(key,
                                                                       new ReliableUdpConnectionRecord(key, this,
                                                                                                       header.
                                                                                                         ReliableUdpMessageType));
        //EN: run state handling
        record.State.ReceivePacket(record, header, bytes);
        hasError = false;
      }
        //RU: EndReceive создает потоки с обработкой каждого пакета
      //RU: завершать его из-за возможных исключений при работе 
      //RU: конечного автомата нецелесообразно, поэтому конечный 
      //RU: автомат инкапсулирует ошибки и передает их в EndInvoke
      //RU: EndReceive может завершиться
      //RU: только если произойдет действительно критическое событие
      //RU: например ошибка сокета, в таком случае мы освобождаем 
      //RU: ресурсы ConnectionControlBlock'а 
        //EN: EndReceive method creates threads where packets are handling
        //EN: we shouldn't stop it if we get an exception in finite state 
        //EN: automaton. So the best idea is to pass this exceptions 
        //EN: directly to invoker. 
        //EN: EndReceive can stop if some critical action will raise in
      //EN: its functions, f.e. socket exceptions on EndReceiveFrom method
      finally
      {
        if (hasError)
        {
          this.Dispose();
        }
      }
    }

    #endregion

    #region Sending

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
    public void SendMessage(ReliableUdpMessage reliableUdpMessage,
                            IPEndPoint remoteEndPoint)
    {
      StartTransmission(reliableUdpMessage, remoteEndPoint,CancellationToken.None, null);
    }

    /// <summary>
    /// Sends the <paramref name="reliableUdpMessage"/> asynchronously to the specified endpoint.
    /// </summary>
    /// <param name="reliableUdpMessage"><see cref="ReliableUdpMessage"/> message to send.</param>
    /// <param name="remoteEndPoint">Ip endpoint of recipient of the message.</param>
    /// <param name="callback">The <see cref="AsyncCallback"/> delegate.</param>
    /// <param name="state">An object that contains state information for this request.</param>
    /// <returns>An IAsyncResult that references the asynchronous send.</returns>
    public IAsyncResult BeginSend(ReliableUdpMessage reliableUdpMessage,
                                  IPEndPoint remoteEndPoint, AsyncCallback callback, Object state)
    {
      //RU: упаковали данные в собственный AsyncResult
      //EN: wrapped data in our own AsyncResult
      AsyncResultSendMessage ar = new AsyncResultSendMessage(reliableUdpMessage, remoteEndPoint,CancellationToken.None, callback, state, this);

      ThreadPool.QueueUserWorkItem(StartTransmissionHelper, ar);
      return ar;
    }

    private IAsyncResult BeginSendTask(WrappedBeginSendParameters obj, AsyncCallback callback, Object state)
    {
      //RU: упаковали данные в собственный AsyncResult
      //EN: wrapped data in our own AsyncResult
      AsyncResultSendMessage ar = new AsyncResultSendMessage( obj.ReliableUdpMessage, obj.RemoteEndPoint,obj.Token, callback, state, this );
      ThreadPool.QueueUserWorkItem( StartTransmissionHelper, ar );
      return ar;
    }

    public Task<bool> SendMessageAsync(ReliableUdpMessage reliableUdpMessage, IPEndPoint remoteEndPoint, CancellationToken cToken)
    {
      TaskFactory<bool> taskFactory = new TaskFactory<bool>(cToken);
      return taskFactory.FromAsync(BeginSendTask, EndSend, new WrappedBeginSendParameters(reliableUdpMessage, remoteEndPoint, cToken),
                                   null);
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
    public bool EndSend(IAsyncResult asyncResult)
    {
      AsyncResultSendMessage ar = (AsyncResultSendMessage) asyncResult;
      return AsyncResultSendMessage.EndInvoke(ar, this);
    }
    
    /// <summary>
    /// Wrapper of StartTransmission method
    /// </summary>
    /// <param name="asyncResult">Parameter to pass through thread pool</param>
    private void StartTransmissionHelper(Object asyncResult)
    {
      AsyncResultSendMessage ar = (AsyncResultSendMessage) asyncResult;
      StartTransmission(ar.ReliableUdpMessage, ar.EndPoint, ar.Token, ar);
    }

    /// <summary>
    /// Creates new ReliableUdpConnectionRecord and starts finite state automaton
    /// </summary>
    /// <param name="reliableUdpMessage"><see cref="ReliableUdpMessage"/> message to send.</param>
    /// <param name="endPoint">Ip endpoint of recipient of the message.</param>
    /// <param name="asyncResult"><see cref="AsyncResultSendMessage"/> object</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ReliableUdpConfigurationException"></exception>
    /// <returns></returns>
    private void StartTransmission(ReliableUdpMessage reliableUdpMessage, EndPoint endPoint, CancellationToken cToken,
                                   AsyncResultSendMessage asyncResult)
    {
      try
      {
        if (m_isListenerStarted == 0)
        {
          if (this.LocalEndpoint == null)
          {
            throw new ArgumentNullException( "", "You must use constructor with parameters or start listener before sending message" );
          }
          StartListener(LocalEndpoint);
        }
        //RU: создаем ключ для словаря, на основе EndPoint и ReliableUdpHeader.TransmissionId
        //EN: create key based on EndPoint and ReliableUdpHeader.TransmissionId for dictionary
        byte[] transmissionId = new byte[4];
        
        //RU: только криптопровайдер! random выдает одинаковые номера для двух одновременно созданных TCB
        //EN: should use only cryptographic random function. System.Random gives the same values for two 
        //EN: or more ConnnectionControlBlock created at the same time
        m_randomCrypto.GetBytes(transmissionId);
        Tuple<EndPoint, Int32> key = new Tuple<EndPoint, Int32>(endPoint, BitConverter.ToInt32(transmissionId, 0));
        Debug.WriteLine("Transmission Id is {0}", key.Item2);
        //EN: make two attemps to add key into dictionary
        if (!m_listOfHandlers.TryAdd(key, new ReliableUdpConnectionRecord(key, this, reliableUdpMessage, cToken, asyncResult)))
        {
          m_randomCrypto.GetBytes(transmissionId);
          key = new Tuple<EndPoint, Int32>(endPoint, BitConverter.ToInt32(transmissionId, 0));
          if (!m_listOfHandlers.TryAdd(key, new ReliableUdpConnectionRecord(key, this, reliableUdpMessage,cToken, asyncResult)))
            throw new ArgumentException("Pair TransmissionId & EndPoint is already exists in the dictionary");
        }
        //RU: запустили состояние в обработку. 
        //EN: run state handling
        m_listOfHandlers[key].State.SendPacket(m_listOfHandlers[key]);
      }
        //RU: Все исключения в состояниях обрабатываются отдельно
        //RU: здесь ловятся только те, которые могут появиться до запуска конечного автомата
        //EN: All exceptions that raised in states are handled separately
      catch (ArgumentNullException ex)
      {
        if (asyncResult != null)
        {
          asyncResult.SetAsCompleted(ex);
        }
        else
        {
          throw;
        }
      }
      catch (ArgumentOutOfRangeException ex)
      {
        if (asyncResult != null)
        {
          asyncResult.SetAsCompleted(ex);
        }
        else
        {
          throw;
        }
      }
      catch (ArgumentException ex)
      {
        if (asyncResult != null)
        {
          asyncResult.SetAsCompleted(ex);
        }
        else
        {
          throw;
        }
      }
      catch (ReliableUdpConfigurationException ex)
      {
        if (asyncResult != null)
        {
          asyncResult.SetAsCompleted(ex);
        }
        else
        {
          throw;
        }
      }
    }


    /// <summary>
    /// Sends udp message
    /// </summary>
    /// <param name="payload">data to transfer</param>
    /// <param name="remoteEp">remote endpoint</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <returns><c>true</c> if all bytes have been sent</returns>
    internal bool SendUdp(byte[] payload, EndPoint remoteEp)
    {
      IAsyncResult ar = m_socketIn.BeginSendTo(payload, 0, payload.Length, SocketFlags.None, remoteEp, null, null);

      int bytesSend = m_socketIn.EndSendTo(ar);
      if (bytesSend == payload.Length)
      {
        return true;
      }
      return false;
    }

    #endregion

    #region Disposing
    /// <summary>
    /// Closes the all connections and releases all associated resources.
    /// </summary>
    public void Dispose()
    {
      try
      {
      }
      finally
      {
        if (m_socketIn != null)
        {
          m_socketIn.Shutdown(SocketShutdown.Both);
          m_socketIn.Close(500);
        }
        if (m_randomCrypto != null)
        {
          m_randomCrypto.Dispose();
        }
      }
    }
    #endregion

    #region Nested types
    /// <summary>
    /// Collection of states of finite state automaton 
    /// </summary>
    public class StatesCollection
    {
      /// <summary>
      /// FirstPacketReceived state
      /// </summary>
      public readonly ReliableUdpState FirstPacketReceived;
      /// <summary>
      /// Assembling state
      /// </summary>
      public readonly ReliableUdpState Assembling;
      /// <summary>
      /// Completed state
      /// </summary>
      public readonly ReliableUdpState Completed;
      /// <summary>
      /// FirstPacketSending state
      /// </summary>
      public readonly ReliableUdpState FirstPacketSending;
      /// <summary>
      /// Sending cicle
      /// </summary>
      public readonly ReliableUdpState SendingCycle;

      /// <summary>
      /// Initialises states
      /// </summary>
      public StatesCollection()
      {
        FirstPacketReceived = new FirstPacketReceived();
        Assembling = new Assembling();
        Completed = new Completed();
        FirstPacketSending = new FirstPacketSending();
        SendingCycle = new SendingCycle();
      }

      /// <summary>
      /// Returns IEnumerator of corresponding type
      /// </summary>
      /// <returns></returns>
      public IEnumerator<ReliableUdpState> GetEnumerator()
      {
        yield return FirstPacketReceived;
        yield return Assembling;
        yield return Completed;
        yield return FirstPacketSending;
        yield return SendingCycle;
      }
    }

    /// <summary>
    /// WrappedBeginSendParameters for wrapping APM in Task
    /// </summary>
    public class WrappedBeginSendParameters
    {
      public ReliableUdpMessage ReliableUdpMessage;
      public IPEndPoint RemoteEndPoint;
      public CancellationToken Token;

      public WrappedBeginSendParameters( ReliableUdpMessage reliableUdpMessage, IPEndPoint remoteEndPoint, CancellationToken cToken )
      {
        ReliableUdpMessage = reliableUdpMessage;
        RemoteEndPoint = remoteEndPoint;
        Token = cToken;
      }
    }
    #endregion
  }
}