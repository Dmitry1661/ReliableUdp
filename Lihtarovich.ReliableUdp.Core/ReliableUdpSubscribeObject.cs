// ******************************************************************************
// Module:  ReliableUdpStateTools.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System;
using System.Net;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Represents a callback method to be executed when <see cref="ReliableUdpMessage"/> received
  /// </summary>
  /// <param name="reliableUdpMessage">The received message. Type of a message is defined in <see cref="ReliableUdpMessageTypes"/> 
  /// </param>
  /// <param name="remoteClient"><see cref="IPEndPoint"/> of the sender of the message</param>
  public delegate void ReliableUdpMessageCallback( ReliableUdpMessage reliableUdpMessage, IPEndPoint remoteClient );
  
  /// <summary>
  /// Represents a subscriber of a <see cref="ReliableUdpMessage"/>
  /// </summary>
  /// <remarks>
  /// The ReliableUdpSubscribeObject returned from  
  /// <see cref="ReliableUdp.SubscribeOnMessages"/> method calling. 
  /// It can be used to unsubsribe from <see cref="ReliableUdpMessage"/> 
  /// by calling <see cref="ReliableUdp.Unsubscribe( ReliableUdpSubscribeObject )"/>
  /// method.</remarks>
  public class ReliableUdpSubscribeObject : IEquatable<ReliableUdpSubscribeObject>
  {
    /// <summary>
    /// Gets a <see cref="IPEndPoint"/> that defines from what IP address and port to receive messages. 
    /// <c>null</c> means any IP address
    /// </summary>
    public IPEndPoint IpEndPoint { get; private set; }
    /// <summary>
    /// Gets a <see cref="ReliableUdpMessageTypes"/> that defines message type to receive.
    /// </summary>
    public ReliableUdpMessageTypes ReliableUdpMessageType { get; private set; }
    /// <summary>
    /// Gets specified callback method
    /// </summary>
    internal ReliableUdpMessageCallback ReliableUdpMessageCallback { get; private set; }
    
    /// <summary>
    /// Initializes a new instance of the ReliableUdpSubscribeObject class using the specified 
    /// callback method, message type and ip endpoint.
    /// </summary>
    /// <param name="callback">Callback method to receive messages</param>
    /// <param name="messageType">Message type to receive </param>
    /// <param name="ipEndPoint">Ip endpoint to receive from </param>
    internal ReliableUdpSubscribeObject(ReliableUdpMessageCallback callback, 
                                      ReliableUdpMessageTypes messageType=ReliableUdpMessageTypes.Any, IPEndPoint ipEndPoint=null )
    {
      IpEndPoint = ipEndPoint;
      ReliableUdpMessageType = messageType;
      ReliableUdpMessageCallback = callback;
    }

    /// <summary>
    /// Determines whether the specified ReliableUdpSubscribeObject is equal to the current ReliableUdpSubscribeObject
    /// </summary>
    /// <param name="other">ReliableUdpSubscribeObject to compare</param>
    /// <returns></returns>
    public bool Equals(ReliableUdpSubscribeObject other)
    {
      return (ReliableUdpMessageCallback == other.ReliableUdpMessageCallback)
             && (ReliableUdpMessageType.Equals(other.ReliableUdpMessageType))
             && (IpEndPoint.Equals(other.IpEndPoint));
    }
  }
}