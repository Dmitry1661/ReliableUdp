// ******************************************************************************
// Module:  ReliableUdpConnectionRecord.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System;
using System.Net;
using System.Threading;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Encapsulates connection data
  /// </summary>
  internal class ReliableUdpConnectionRecord : IDisposable
  {
    /// <summary>
    /// Gets or sets byte array for incoming message
    /// </summary>
    public byte[] IncomingStream { get; set; }

    /// <summary>
    /// Gets or sets byte array for outcoming message
    /// </summary>
    public byte[] OutcomingStream { get; private set; }

    /// <summary>
    /// Gets or sets state of finite state automaton
    /// </summary>
    public ReliableUdpState State { get; set; }

    /// <summary>
    /// Gets Key ( pair EndPoint + Int32 ) 
    /// </summary>
    public Tuple<EndPoint, int> Key { get; private set; }

    /// <summary>
    /// Window lower bound
    /// </summary>
    public int WindowLowerBound;

    /// <summary>
    /// Transmission window size
    /// </summary>
    public readonly int WindowSize;


    /// <summary>
    /// Gets window control array. Size of array is <see cref="WindowSize"/>
    /// </summary>
    public int[] WindowControlArray { get; private set; }

    //RU: Текущий отправленный пакет
    /// <summary>
    /// Current sent package
    /// </summary>
    public int SndNext;

    //RU:Количество пакетов для отправки. Оставил открытым. Не изменять!
    /// <summary>
    /// Number of packets for sending. I left it public. Do not change!
    /// </summary>
    public int NumberOfPackets;

    /// <summary>
    /// Transmission number
    /// </summary>
    public readonly Int32 TransmissionId;

    /// <summary>
    /// Remote ip endpoint
    /// </summary>
    public readonly IPEndPoint RemoteClient;

    /// <summary>
    /// Do not send asknowledge packets
    /// </summary>
    public bool IsNoAnswerNeeded;

    /// <summary>
    /// The last correctly received package (always takes the maximum number)
    /// </summary>
    public int RcvCurrent;

    /// <summary>
    /// Gets array with lost packets numbers
    /// </summary>
    public int[] LostPackets { get; private set; }

    /// <summary>
    /// Size of transmission packet  
    /// </summary>
    public readonly int BufferSize;

    /// <summary>
    /// Is last packet received. Acts as boolean. Sets to 1 if packet has flag LastPacket
    /// </summary>
    public int IsLastPacketReceived = 0;

    /// <summary>
    /// The intermediate counter of packages, is used for delivery check
    /// It is nullified when equals to RcvPacketsMax
    /// </summary>
    public int PacketCounter;

    #region Timers

    /// <summary>
    /// Timer interval before retransmission request
    /// </summary>
    public readonly int ShortTimerPeriod;

    /// <summary>
    /// Timer interval before release of resources in case of probable interrupted transmitting 
    /// </summary>
    public readonly int LongTimerPeriod;

    /// <summary>
    /// The timer, for automatic check 
    /// </summary>
    public System.Threading.Timer WaitForPacketsTimer;

    /// <summary>
    /// The timer working at long expectation
    /// </summary>
    public System.Threading.Timer CloseWaitTimer;

    #endregion

    /// <summary>
    /// Connection control block. Core of ReliableUdp
    /// </summary>
    public readonly ReliableUdpConnectionControlBlock Tcb;

    /// <summary>
    /// Object for blocking. To use intelligently!!
    /// </summary>
    public readonly Object LockerReceive;

    /// <summary>
    /// Additional object for elimination of mistakes with reception of the duplicating packets
    /// </summary>
    public int IsDone;

    /// <summary>
    /// Cancellation Token
    /// </summary>
    public CancellationToken CToken;

    /// <summary>
    /// <see cref="AsyncResultSendMessage"/>
    /// </summary>
    public readonly AsyncResultSendMessage AsyncResult;

    /// <summary>
    /// Gets <see cref="ReliableUdpMessageType"/>
    /// </summary>
    public ReliableUdpMessageTypes ReliableUdpMessageType { get; private set; }

    /// <summary>
    /// Second try for WaitForPacketsTimer
    /// </summary>
    public bool TimerSecondTry;

    /// <summary>
    /// Initializes a new instance of the ReliableUdpConnectionRecord
    /// </summary>
    /// <param name="key">EndPoint and TransmissionId key</param>
    /// <param name="tcb">Connection control block. <see cref="ReliableUdpConnectionControlBlock"/></param>
    private ReliableUdpConnectionRecord(Tuple<EndPoint, Int32> key, ReliableUdpConnectionControlBlock tcb)
    {
      if (key == null)
      {
        throw new ArgumentNullException("key");
      }
      if (key.Item1 == null)
      {
        throw new ArgumentNullException("key", "EndPoint can not be null. No remoteClient is configured.");
      }
      if (tcb == null)
      {
        throw new ArgumentNullException("tcb");
      }
      PacketCounter = 0;
      this.IsDone = 0;
      this.Key = key;
      this.Tcb = tcb;
      this.WindowSize = tcb.TransmittingWindowSize;
      this.BufferSize = tcb.DefaultMaxPacketSize;
      this.TransmissionId = key.Item2;
      this.RemoteClient = (IPEndPoint) key.Item1;
      this.WindowControlArray = new int[this.WindowSize];
      this.LostPackets = new int[this.WindowSize];
      this.ShortTimerPeriod = tcb.ShortTimerPeriod;
      this.LongTimerPeriod = tcb.LongTimerPeriod;
      this.LockerReceive = new object();
    }

    /// <summary>
    /// Initializes a new instance of the ReliableUdpConnectionRecord
    /// to receive message 
    /// </summary>
    /// <param name="key">EndPoint and TransmissionId key</param>
    /// <param name="tcb">Connection control block. <see cref="ReliableUdpConnectionControlBlock"/></param>
    /// <param name="reliableUdpMessageType">Type of message to receive</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="tcb"/> is a null reference</exception>
    public ReliableUdpConnectionRecord(Tuple<EndPoint, Int32> key, ReliableUdpConnectionControlBlock tcb,
                                       ReliableUdpMessageTypes reliableUdpMessageType)
      : this(key, tcb)
    {
      CToken = CancellationToken.None;
      this.ReliableUdpMessageType = reliableUdpMessageType;
      //set initial state
      State = Tcb.States.FirstPacketReceived;
    }

    /// <summary>
    /// Initializes a new instance of the ReliableUdpConnectionRecord
    /// to send message 
    /// </summary>
    /// <param name="key">EndPoint and TransmissionId key</param>
    /// <param name="tcb">Connection control block. <see cref="ReliableUdpConnectionControlBlock"/></param>
    /// <param name="reliableUdpMessage"><see cref="ReliableUdpMessage"/> message to send.</param>
    /// <param name="cToken">CancellationToken</param>
    /// <param name="asyncResult"><see cref="AsyncResultSendMessage"/></param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="tcb"/> is a null reference</exception>
    public ReliableUdpConnectionRecord(Tuple<EndPoint, Int32> key, ReliableUdpConnectionControlBlock tcb,
                                       ReliableUdpMessage reliableUdpMessage, CancellationToken cToken, AsyncResultSendMessage asyncResult)
      : this(key, tcb)
    {
      if (reliableUdpMessage == null)
      {
        throw new ArgumentNullException("reliableUdpMessage");
      }
      if (reliableUdpMessage.Body == null || reliableUdpMessage.Body.Length == 0)
      {
        throw new ArgumentNullException("reliableUdpMessage", "reliableUdpMessage.Body can not be null.");
      }
      CToken = cToken;
      this.AsyncResult = asyncResult;
      this.ReliableUdpMessageType = reliableUdpMessage.Type;
      this.IsNoAnswerNeeded = reliableUdpMessage.NoAsk;
      //RU: добавляем содержимое ReliableUdpMessage в словарь ReliableUdpConnectionControlBlock'a
      //EN: fills the array of outcoming message with message bytes
      this.OutcomingStream = Tcb.OutcomingStreams.GetOrAdd(key, reliableUdpMessage.Body);
      //RU: Расчитываем количество пакетов необходимых для отправки сообщения
      this.NumberOfPackets = (int) Math.Ceiling((double) ((double) OutcomingStream.Length/(double) this.BufferSize));
      //RU: переключаем состояние на отправку первого пакета
      //EN: set initial state
      this.State = Tcb.States.FirstPacketSending;
    }

    /// <summary>
    /// Releases associated resources
    /// </summary>
    public void Dispose()
    {
      try
      {
        System.Threading.Monitor.Enter(this.LockerReceive);
      }
      finally
      {
        Interlocked.Increment(ref this.IsDone);
        if (WaitForPacketsTimer != null)
        {
          WaitForPacketsTimer.Dispose();
        }

        if (CloseWaitTimer != null)
        {
          CloseWaitTimer.Dispose();
        }

        byte[] stream;
        Tcb.IncomingStreams.TryRemove(Key, out stream);
        stream = null;
        Tcb.OutcomingStreams.TryRemove(Key, out stream);
        stream = null;
        System.Threading.Monitor.Exit(this.LockerReceive);
      }
    }
  }
}