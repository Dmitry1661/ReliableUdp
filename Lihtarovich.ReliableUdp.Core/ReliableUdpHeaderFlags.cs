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
    //RU: ������ ����� 
    /// <summary>
    /// First packet of a message
    /// </summary>
    FirstPacket = 0x01,
    //RU: ����� �� ������� �������������
    /// <summary>
    /// Packet doesn't need acknowledge
    /// </summary>
    NoAsk = 0x02,
    //RU:����������� ����� ��� ���������
    /// <summary>
    /// Last packet of a message
    /// </summary>
    LastPacket = 0x04,
    //RU: ������ ������ ��� ����� �������������
    /// <summary>
    /// Request for lost or acknowledge packet
    /// </summary>
    RequestForPacket = 0x08,
  }
}