// ******************************************************************************
// Module:  ReliableUdpMessage.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Reliable Udp message
  /// </summary>
  public class ReliableUdpMessage
  {
    /// <summary>
    /// Type of message. See <see cref="ReliableUdpMessageTypes"/>
    /// </summary>
    public ReliableUdpMessageTypes Type { get; private set; }

    /// <summary>
    /// Message body. Any data you want to send via Reliable Udp
    /// </summary>
    public byte[] Body { get; private set; }

    /// <summary>
    /// If it is set to <c>true</c> that the mechanism of confirmation of delivery will be switched-off
    /// </summary>
    /// <remarks>
    /// You can set this property to <c>true</c> to send single packets one third faster. 
    /// Or if reliable delivery isn't required for some part of your application.
    /// Anyway, if you set this property to <c>true</c>, acknowledge packages won't come from the recipient of the message.
    /// </remarks>
    public bool NoAsk { get; private set; }

    /// <summary>
    /// Initializes a new instance of the ReliableUdpMessage 
    /// </summary>
    /// <param name="messageTypes">Type of the message</param>
    /// <param name="body">Message body (in bytes)</param>
    /// <param name="noAsk">Not to use the mechanism of reliable delivery.</param>
    public ReliableUdpMessage(ReliableUdpMessageTypes messageTypes, byte[] body, bool noAsk = false)
    {
      this.Type = messageTypes;
      this.Body = body;
      this.NoAsk = noAsk;
    }
  }
}