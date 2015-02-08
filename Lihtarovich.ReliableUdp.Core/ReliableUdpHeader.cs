// ******************************************************************************
// Module:  ReliableUdpHeader.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Reliable Udp Header
  /// </summary>
  /// <remarks>
  /// Header format:  
  /// =====================================
  /// | 0 byte | 1 byte | 2 byte | 3 byte |
  /// =====================================
  /// | Flags  | empty  |   MessageType   | 0-3 bytes
  ///  -----------------------------------
  /// |          TransmissionID           | 4-7 bytes
  ///  -----------------------------------
  /// |          PacketNumber             | 8-11 bytes
  ///  -----------------------------------
  /// |             Options               | 12-15 bytes
  /// ===================================== 
  /// </remarks>
  internal class ReliableUdpHeader
  {
    /*Header format:  
     * =====================================
     * | 0 byte | 1 byte | 2 byte | 3 byte |
     * =====================================
     * | Flags  | empty  |   MessageType   | 0-3 bytes
     *  -----------------------------------
     * |          TransmissionID           | 4-7 bytes
     *  -----------------------------------
     * |          PacketNumber             | 8-11 bytes
     *  -----------------------------------
     * |             Options               | 12-15 bytes
     * ===================================== 
     * 
     * 
     */

    /// <summary>
    /// Header flags. See <see cref="ReliableUdpHeaderFlags"/>
    /// </summary>
    public ReliableUdpHeaderFlags Flags;

    /// <summary>
    /// Type of message. See <see cref="ReliableUdpMessageTypes"/>
    /// </summary>
    public ReliableUdpMessageTypes ReliableUdpMessageType;

    //RU: идентификатор передачи данных. Одно сообщение - один идентификатор
    /// <summary>
    /// Transmission Id. One message - one id
    /// </summary>
    public Int32 TransmissionId;

    /// <summary>
    /// Number of the sent packet.
    /// </summary>
    public Int32 PacketNumber;

    //RU: Опции: для Flags.FirstMessage - это длина сообщения
    /// <summary>
    /// Options. Stores the message size in the packet with FirstPacket.Flag
    /// </summary>
    public Int32 Options;

    /// <summary>
    /// Header length
    /// </summary>
    public const Int32 Length = 16;
  }
}