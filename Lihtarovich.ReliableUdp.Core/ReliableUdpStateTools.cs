// ******************************************************************************
// Module:  ReliableUdpStateTools.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Provides static methods for ReliableUdpState states.
  /// </summary>
  internal static class ReliableUdpStateTools
  {
    /// <summary>
    /// Reads ReliableUdp header
    /// </summary>
    /// <param name="udpPayloadBytes">Payload of udp packet</param>
    /// <param name="header"><see cref="ReliableUdpHeader"/></param>
    /// <returns><c>false</c> if packet is garbage or damaged</returns>
    internal static bool ReadReliableUdpHeader(byte[] udpPayloadBytes,
                                               out ReliableUdpHeader header)
    {
      header = new ReliableUdpHeader();
      //RU: в случае поврежденного пакета
      //EN: in case of damaged packet
      if (udpPayloadBytes.Length < ReliableUdpHeader.Length)
        return false;

      header.Flags = (ReliableUdpHeaderFlags) udpPayloadBytes[0];
      header.ReliableUdpMessageType = (ReliableUdpMessageTypes) BitConverter.ToInt16(udpPayloadBytes, 2);
      header.TransmissionId = BitConverter.ToInt32(udpPayloadBytes, 4);
      header.PacketNumber = BitConverter.ToInt32(udpPayloadBytes, 8);

      //RU: здесь мы проверяем первый пакет из множества пакетов, передающих сообщение
      //RU: узнаем размер сообщения, размер нам необходим для создания MemoryStream'а      
      //EN: here we check first packet with data and get total size of message from
      //EN: Options field
      if (header.Flags.HasFlag(ReliableUdpHeaderFlags.FirstPacket)
          && (!header.Flags.HasFlag(ReliableUdpHeaderFlags.RequestForPacket))
        )
      {
        header.Options = BitConverter.ToInt32(udpPayloadBytes, 12);

        if (header.Options < 1)
        {
          //EN: drop damaged packet
          return false;
        }
      }
      return true;
    }

    internal static ReliableUdpHeader CreateReliableUdpHeader(Int32 transmissionId,
                                                              ReliableUdpHeaderFlags flags,
                                                              ReliableUdpMessageTypes reliableUdpMessageType,
                                                              Int32 sequenceNumber, Int32 options)
    {
      ReliableUdpHeader header = new ReliableUdpHeader
                                   {
                                     TransmissionId = transmissionId,
                                     Flags = flags,
                                     ReliableUdpMessageType = reliableUdpMessageType,
                                     PacketNumber = sequenceNumber,
                                     Options = options,
                                   };
      return header;
    }

    /// <summary>
    /// Creates Reliable Udp header from connectionRecord
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <returns></returns>
    internal static ReliableUdpHeader CreateReliableUdpHeader(ReliableUdpConnectionRecord connectionRecord)
    {
      ReliableUdpHeader header = new ReliableUdpHeader();
      //RU: запись номера передачи
      header.TransmissionId = connectionRecord.TransmissionId;
      //RU: тип сообщения
      header.ReliableUdpMessageType = connectionRecord.ReliableUdpMessageType;
      //RU: установка номера пакета для передачи
      header.PacketNumber = connectionRecord.SndNext;

      //RU: запись управляющих флагов
      ReliableUdpHeaderFlags flags = ReliableUdpHeaderFlags.None;
      //RU: выставляем комбинацию двух флагов для единственного сообщения
      //EN: set flags for single packet message
      if (connectionRecord.NumberOfPackets == 1)
      {
        flags = ReliableUdpHeaderFlags.FirstPacket | ReliableUdpHeaderFlags.LastPacket;
      }
      //RU: дополнительный флаг если нам не требуется подтверждение
      //EN: set additional flag if we don't need to use acknowledge mechanism
      if (connectionRecord.IsNoAnswerNeeded)
      {
        flags = flags | ReliableUdpHeaderFlags.NoAsk;
      }
      //RU: это первый пакет
      //EN: check for first packet
      if (connectionRecord.NumberOfPackets > 1 && connectionRecord.SndNext == 0)
      {
        flags = flags | ReliableUdpHeaderFlags.FirstPacket;
      }
      //RU: это последний пакет
      //EN: check for last packet
      if (connectionRecord.NumberOfPackets > 1
          && (connectionRecord.NumberOfPackets - connectionRecord.SndNext - 1 == 0))
      {
        flags = flags | ReliableUdpHeaderFlags.LastPacket;
      }
      //RU: записываем флаги
      header.Flags = flags;

      //RU: запись поля Options - размер всего сообщения
      //EN: if we've got first message we should write total size of message
      //EN: in header.Options
      if (header.Flags.HasFlag(ReliableUdpHeaderFlags.FirstPacket))
      {
        header.Options = connectionRecord.OutcomingStream.Length;
      }
      return header;
    }

    /// <summary>
    /// Gets byte array from ReliableUdpHeader
    /// </summary>
    /// <param name="header"></param>
    /// <returns></returns>
    internal static byte[] ToBytes(this ReliableUdpHeader header)
    {
      byte[] bytes = new byte[ReliableUdpHeader.Length];
      bytes[0] = (byte) header.Flags;
      Array.Copy(BitConverter.GetBytes((short) header.ReliableUdpMessageType), 0, bytes, 2, 2);
      Array.Copy(BitConverter.GetBytes(header.TransmissionId), 0, bytes, 4, 4);
      Array.Copy(BitConverter.GetBytes(header.PacketNumber), 0, bytes, 8, 4);
      Array.Copy(BitConverter.GetBytes(header.Options), 0, bytes, 12, 4);
      return bytes;
    }

    /// <summary>
    /// Initialises new byte array to save message packets data
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="header"><see cref="ReliableUdpHeader"/></param>   
    internal static void InitIncomingBytesStorage(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header)
    {
      //RU: Параметры проверены на этапе приема пакета и создания connectionRecord  

      //RU: здесь я оставляю макисмально возможный размер массива доступным. Хотя 
      //RU: вышестоящие протоколам не следует пользоваться этой возможностью без 
      //RU: острой на то необходимости

      //RU: пояснение к Header.Options - для первого пакета (c флагом FirstPacket) мы указываем длину
      //RU: передаваемого сообщения в поле Header.Options

      //EN: all parameters are checked when we received packets and created connectionRecord.

      //EN: here I decided to allow maximum size of byte array. Although, the 
      //EN: upper-level protocols should not use this feature without need

      //EN: about header.Options - in the first transmitting packet we set this field to size of message
      connectionRecord.IncomingStream = connectionRecord.Tcb.IncomingStreams.GetOrAdd(connectionRecord.Key,
                                                                                      new byte[header.Options]);
    }

    /// <summary>
    /// Writes packet data to byte array
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="header"><see cref="ReliableUdpHeader"/></param>
    /// <param name="data">Udp payload</param>
    internal static void WritePacketData(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header,
                                         byte[] data)
    {
      //RU: Т.к. мы используем пакеты сторого фиксированного размера
      //RU: и при отправке разбитого на части Сообщения отправляем все пакеты,
      //RU: кроме последнего, будут одного размера, то спокойно можем использовать
      //RU: Header.PacketNumber* connectionRecord.BufferSize для указания позиции записи.
      //EN: We use fixed-size packets to send all packets except last one. So 
      //EN: We can easily establish a position for data recording by this formula: Header.PacketNumber* connectionRecord.BufferSize 
      Array.Copy(data, ReliableUdpHeader.Length, connectionRecord.IncomingStream,
                 header.PacketNumber*connectionRecord.BufferSize, data.Length - ReliableUdpHeader.Length);
    }

    /// <summary>
    /// Checks for no packet loss
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="hasCompletedState">If state is <see cref="Completed"/> set <c>true</c></param>
    /// <returns><c>true</c> if there is no loss</returns>
    internal static bool CheckForNoPacketLoss(ReliableUdpConnectionRecord connectionRecord, bool hasCompletedState)
    {
      //RU: в управляющий массив WindowControlArray все пришедший пакеты заносятся
      //RU: в том порядке, в каком они бы пришли последовательно:
      //RU:
      //RU: Пример:
      //RU: у нас размер окна 5. Нижняя граница выставлена в 6.
      //RU: connectionRecord.WindowSize = 5
      //RU: connectionRecord.WindowLowerBound = 6 (всегда сдивгается на 1 при получении первого пакета, +5 стало 6)
      //RU: S(sender) --> udp#6  --> R(receiver) ; connectionRecord.WindowControlArray[6 - 6] = 6; //заносит в 0 слот
      //RU: S         --> udp#8  --> R           ; connectionRecord.WindowControlArray[8 - 6] = 8; //заносит в 2 слот
      //RU: S         --> udp#9L --> R           ; connectionRecord.WindowControlArray[9 - 6] = 8; //заносит в 3 слот
      //RU:
      //RU: Из примера видно, что слот connectionRecord.WindowControlArray[1] будет пустым. Следовательно
      //RU: это пропущенный пакет. Для восстановления его номера, 
      //RU: необходимо сложить его индекс с нижней границей окна:
      //RU: connectionRecord.RcvWindowLowerBound + i = 6 + 1 =7;
      //RU: 
      //RU: В случае проверки пакета с флагом Last:
      //RU: переменная connectionRecord.RcvCurrent хранит наибольший номер пришедшего пакета. 
      //RU: Последний пакет всегда имеет наибольший номер, следовательно 
      //RU: проверять массив connectionRecord.WindowControlArray следует только до этого номера

      //EN: All received packets fill WindowControlArray consequentially like there are no packet loss.
      //EN: F.e.:
      //EN: Transmission window size is 5. Window lower bound is 6.
      //EN: connectionRecord.WindowSize = 5
      //EN: connectionRecord.WindowLowerBound = 6 (it always shifts on 1 when we have received first packet, +5 = 6)
      //EN: S(sender) --> udp#6  --> R(receiver) ; connectionRecord.WindowControlArray[6 - 6] = 6; //fills 0 cell
      //EN: S         --> udp#8  --> R           ; connectionRecord.WindowControlArray[8 - 6] = 8; //fills 2 cell
      //EN: S         --> udp#9L --> R           ; connectionRecord.WindowControlArray[9 - 6] = 8; //fills 3 cell
      //EN:
      //EN: In this example we see that connectionRecord.WindowControlArray[1] will be empty. Therefore we have 
      //EN: packet loss. To recover packet number we should combine this index with WindowLowerBound.
      //EN: 
      //EN: In case of LastPacket flag:
      //EN: The connectionRecord.RcvCurrent variable stores the greatest number of the received packet.
      //EN: The last packet always has the greatest number. Therefore we have to check the WindowControlArray
      //EN: only to this number


      bool result = true;
      int j = 0;
      int endOfArray = hasCompletedState
                         ? connectionRecord.NumberOfPackets - connectionRecord.RcvCurrent
                         : connectionRecord.WindowControlArray.Length;
      for (int i = 0; i < endOfArray; i++)
      {
        if (connectionRecord.WindowControlArray[i] == 0)
        {
          result = false;
          //RU: заносим полученные пакеты в массив потерянных пакетов
          //EN: fill LostPackets array with numbers of lost packets
          connectionRecord.LostPackets[j++] = i + connectionRecord.WindowLowerBound;
        }
      }
      return result;
    }

    /// <summary>
    /// Creates udp payload
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="header"><see cref="ReliableUdpHeader"/></param>
    /// <returns>Array of bytes with header and data</returns>
    internal static byte[] CreateUdpPayload(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header)
    {
      //byte[] bytes = Header.Flags.HasFlag(ReliableUdpHeaderFlags.LastPacket)
      //  ? new byte[connectionRecord.OutcomingStream.Length - connectionRecord.RcvCurrent*connectionRecord.BufferSize]
      //  :new byte[connectionRecord.BufferSize];

      //Array.Copy(connectionRecord.OutcomingStream,connectionRecord.BufferSize * connectionRecord.SndCurrent,bytes,0,bytes.Length);

      //byte[] udpPayload = new byte[bytes.Length + ReliableUdpHeader.Length];
      //Array.Copy( Header.ToBytes(), udpPayload, ReliableUdpHeader.Length );
      //Array.Copy( bytes, 0, udpPayload, 0, bytes.Length );

      int length = header.Flags.HasFlag(ReliableUdpHeaderFlags.LastPacket)
                     ? connectionRecord.OutcomingStream.Length - connectionRecord.SndNext*connectionRecord.BufferSize
                     : connectionRecord.BufferSize;

      byte[] udpPayload = new byte[length + ReliableUdpHeader.Length];
      Array.Copy(header.ToBytes(), udpPayload, ReliableUdpHeader.Length);
      Array.Copy(connectionRecord.OutcomingStream, connectionRecord.BufferSize*connectionRecord.SndNext, udpPayload,
                 ReliableUdpHeader.Length, length);

      return udpPayload;
    }

    /// <summary>
    /// Creates a package for a resending
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="requestedSequence">Lost packet number </param>
    /// <returns>Array of bytes with header and data</returns>
    internal static byte[] RetransmissionCreateUdpPayload(ReliableUdpConnectionRecord connectionRecord,
                                                          int requestedSequence)
    {
      //RU: проверка на последний пакет,
      //EN: check for last packet
      //bool isLast = ((RequestedSequence + 1) - connectionRecord.NumberOfPackets == 0);
      bool isLast = ((requestedSequence + 1) == connectionRecord.NumberOfPackets);
      //RU: установка размера данных для отправки
      //EN: set the size of data for sending
      int length = isLast
                     ? connectionRecord.OutcomingStream.Length - requestedSequence*connectionRecord.BufferSize
                     : connectionRecord.BufferSize;
      //RU: создаем заголовок для пакета
      ReliableUdpHeader header = CreateReliableUdpHeader(connectionRecord.TransmissionId,
                                                         isLast
                                                           ? ReliableUdpHeaderFlags.LastPacket
                                                           : ReliableUdpHeaderFlags.None,
                                                         connectionRecord.ReliableUdpMessageType, requestedSequence, 0);
      byte[] udpPayload = new byte[length + ReliableUdpHeader.Length];
      Array.Copy(header.ToBytes(), udpPayload, ReliableUdpHeader.Length);
      Array.Copy(connectionRecord.OutcomingStream, connectionRecord.BufferSize*requestedSequence, udpPayload,
                 ReliableUdpHeader.Length, length);

      return udpPayload;
    }

    /// <summary>
    /// Creates header of acknowledge packet
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/></param>
    /// <param name="isAskForLost"><c>true</c> if we need resending</param>
    /// <param name="seqNum">Lost packet number if <paramref name="isAskForLost"/> is <c>true</c></param>
    /// <returns></returns>
    internal static ReliableUdpHeader CreateAcknowledgeHeader(ReliableUdpConnectionRecord connectionRecord,
                                                              bool isAskForLost = false, int seqNum = -1)
    {
      ReliableUdpHeader header = new ReliableUdpHeader();
      //RU: запись номера передачи
      header.TransmissionId = connectionRecord.TransmissionId;
      //RU: тип сообщения
      header.ReliableUdpMessageType = connectionRecord.ReliableUdpMessageType;
      //RU: установка Ask флаг
      //EN: set Ask flag
      header.Flags = ReliableUdpHeaderFlags.RequestForPacket;
      //RU: для первого пакета устанавливаем соответствующий флаг
      //EN: set FirstPacket flag for the ask on first packet
      if (connectionRecord.RcvCurrent == 0)
      {
        header.Flags = header.Flags | ReliableUdpHeaderFlags.FirstPacket;
      }
      if (!isAskForLost)
      {
        //RU: установка номера пакета для передачи - означает что ждем следующий пакет
        //EN: it means that we are ready and waiting for next block of data
        header.PacketNumber = connectionRecord.RcvCurrent + 1;
      }
      else
      {
        header.PacketNumber = seqNum;
      }
      return header;
    }

    /// <summary>
    /// Sends acknowledge
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="SocketException"></exception>
    internal static void SendAcknowledgePacket(ReliableUdpConnectionRecord connectionRecord,
                                               bool isCheckForLastOk = false)
    {
      //Debug.WriteLine( "To {0}. Ask {1} | connectionRecord.RcvCurrent {2} | connectionRecord.WLB {3}", connectionRecord.RemoteClient, connectionRecord.RcvCurrent + 1, connectionRecord.RcvCurrent, connectionRecord.WindowLowerBound );     
      ReliableUdpHeader header = CreateAcknowledgeHeader(connectionRecord);
      if (isCheckForLastOk)
        header.Flags = header.Flags | ReliableUdpHeaderFlags.LastPacket;

      connectionRecord.Tcb.SendUdp(header.ToBytes(), connectionRecord.RemoteClient);
    }

    /// <summary>
    /// Sends a request for receiving the lost packets
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="seqNum">Lost packet number</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <returns></returns>
    internal static void SendAskForLostPacket(ReliableUdpConnectionRecord connectionRecord, int seqNum)
    {
      //Debug.WriteLine( "To {0}. Request {1} | connectionRecord.RcvCurrent {2} | connectionRecord.WLB {3}", connectionRecord.RemoteClient, seqNum, connectionRecord.RcvCurrent, connectionRecord.WindowLowerBound );  
      connectionRecord.Tcb.SendUdp(CreateAcknowledgeHeader(connectionRecord, true, seqNum).ToBytes(),
                                   connectionRecord.RemoteClient);
    }

    /// <summary>
    /// Sends packet to remote client
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="payload">Header + Payload</param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="SocketException"></exception>
    /// <returns></returns>
    internal static bool SendPacket(ReliableUdpConnectionRecord connectionRecord, byte[] payload)
    {
      //Debug.WriteLine( "To {0}. {2} of Total:{1} <Sending {3}-{6} bytes of {4} > | connectionRecord.SndNext {5} ", 
      //connectionRecord.RemoteClient, connectionRecord.NumberOfPackets, connectionRecord.SndNext, connectionRecord.SndNext * connectionRecord.BufferSize, connectionRecord.OutcomingStream.Length,
      //connectionRecord.SndNext, connectionRecord.SndNext * connectionRecord.BufferSize + Payload.Length );     
      return connectionRecord.Tcb.SendUdp(payload, connectionRecord.RemoteClient);
    }

    /// <summary>
    /// Creates message from single packet
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    /// <param name="header"><see cref="ReliableUdpHeader"/></param>
    /// <param name="data">packet data (udp payload - header)</param>
    internal static void CreateMessageFromSinglePacket(ReliableUdpConnectionRecord connectionRecord,
                                                       ReliableUdpHeader header, byte[] data)
    {
      connectionRecord.Tcb.PassMessageToSubscribers(new ReliableUdpMessage(header.ReliableUdpMessageType,
                                                                           data,
                                                                           header.Flags.HasFlag(
                                                                             ReliableUdpHeaderFlags.NoAsk)),
                                                    connectionRecord.RemoteClient);
    }

    /// <summary>
    /// Creates message
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/> (ConnectionRecord)</param>
    internal static void CreateMessageFromMemoryStream(ReliableUdpConnectionRecord connectionRecord)
    {
      ReliableUdpMessage msg = new ReliableUdpMessage(connectionRecord.ReliableUdpMessageType,
                                                      connectionRecord.Tcb.IncomingStreams[connectionRecord.Key],
                                                      connectionRecord.IsNoAnswerNeeded);
      connectionRecord.Tcb.PassMessageToSubscribers(msg, connectionRecord.RemoteClient);
      Debug.WriteLine("Done {0}!", connectionRecord.TransmissionId);
    }
  }
}