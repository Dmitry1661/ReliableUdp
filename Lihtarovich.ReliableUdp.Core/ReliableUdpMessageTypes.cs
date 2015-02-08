// ******************************************************************************
// Module:  ReliableUdpMessageTypes.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************
namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Reliable Udp message types
  /// </summary>
  public enum ReliableUdpMessageTypes : short
  { 
    /// <summary>
    /// Any
    /// </summary>
    Any = 0,
    /// <summary>
    /// Request to STUN server 
    /// </summary>
    StunRequest = 1,
    /// <summary>
    /// Response from STUN server
    /// </summary>
    StunResponse = 2,
    /// <summary>
    /// File transfer
    /// </summary>
    FileTransfer =3,
    
    //RU: Не реализовано. управляющие сообщения ReliableUdpConnectionControlBlock
    //RU: используются для согласования макс.размеров пакета, размеров окна передачи
    //EN: Not implemented. Control message for Reliable Udp protocol
    //EN: used for coordination of the max. sizes of a package, the transfer window sizes
    /// <summary>
    /// Control message for Reliable Udp protocol
    /// </summary>
    ReliableUdpControl =255,
  }
}