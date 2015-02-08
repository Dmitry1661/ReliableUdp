// ******************************************************************************
// Module:  ReliableUdpHeaderFlags.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Reliable Udp header flags
  /// </summary>
  [Flags]
  internal enum ReliableUdpHeaderFlags : short
  {
    /// <summary>
    /// None
    /// </summary>
    None = 0,
    //RU: Первый пакет 
    /// <summary>
    /// First packet of a message
    /// </summary>
    FirstPacket = 0x01,
    //RU: Пакет не требует подтверждения
    /// <summary>
    /// Packet doesn't need acknowledge
    /// </summary>
    NoAsk = 0x02,
    //RU:Завершающий пакет для сообщения
    /// <summary>
    /// Last packet of a message
    /// </summary>
    LastPacket = 0x04,
    //RU: Запрос пакета или пакет подтверждения
    /// <summary>
    /// Request for lost or acknowledge packet
    /// </summary>
    RequestForPacket = 0x08,
  }
}