// ******************************************************************************
// Module:  ReliableUdpState.cs
// Notices: Written by Kirill Likhtarovich
// ******************************************************************************

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Implements the finite-state machine of ReliableUdp protocol. This is an abstract class
  /// </summary>
  internal abstract class ReliableUdpState
  {
    public virtual void ReceivePacket(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header,
                                      byte[] payload)
    {
    }

    public virtual void ProcessPackets(ReliableUdpConnectionRecord connectionRecord)
    {
    }

    public virtual void SendPacket(ReliableUdpConnectionRecord connectionRecord)
    {
    }

    //RU: Выполняется проверка пакетов при срабатывании таймера
    /// <summary>
    /// Inspects packets when the timer fires
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/></param>
    protected void CheckByTimer(object connectionRecord)
    {
      ReliableUdpConnectionRecord record = (ReliableUdpConnectionRecord) connectionRecord;
      //Debug.WriteLine( "CheckByTimer invoked in state {0}", connectionRecord.State.ToString() );
      record.State.ProcessPackets(record); //RU: запустится в нужном State
    }

    /// <summary>
    /// Reports about successful sending
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/></param>
    protected static void SetAsCompleted(ReliableUdpConnectionRecord connectionRecord)
    {
      if (connectionRecord.AsyncResult != null)
      {
        connectionRecord.AsyncResult.SetAsCompleted(true);
      }
      //RU: пока не произойдет метод EndInvoke для AsyncResult'а
      //RU: connectionRecord будет висеть в памяти, но 
      //RU: можно освободить другие ресурсы
      connectionRecord.Dispose();
    }

    /// <summary>
    /// Starts or changes CloseWaitTimer
    /// </summary>
    /// <param name="connectionRecord"></param>
    protected void StartCloseWaitTimer(ReliableUdpConnectionRecord connectionRecord)
    {
      if (connectionRecord.CloseWaitTimer != null)
        connectionRecord.CloseWaitTimer.Change(connectionRecord.LongTimerPeriod, -1);
      else
        connectionRecord.CloseWaitTimer = new Timer(DisposeByTimeout, connectionRecord,
                                                    connectionRecord.LongTimerPeriod, -1);
    }

    /// <summary>
    /// Reports about error during sending
    /// </summary>
    /// <param name="connectionRecord">Current <see cref="ReliableUdpConnectionRecord"/></param>
    /// <param name="ex">The thrown-out exception</param>
    protected static void SetAsError(ReliableUdpConnectionRecord connectionRecord, Exception ex)
    {
      if (connectionRecord.AsyncResult != null)
      {
        connectionRecord.AsyncResult.SetAsCompleted(ex);
      }
      else
      {
        throw ex;
      }
      connectionRecord.Dispose();
    }

    /// <summary>
    /// Reports about unsuccessful sending due to timeout
    /// </summary>
    /// <param name="connectionRecord">object as <see cref="ReliableUdpConnectionRecord"/></param>
    protected virtual void DisposeByTimeout(object connectionRecord)
    {
      ReliableUdpConnectionRecord record = (ReliableUdpConnectionRecord) connectionRecord;
      Debug.WriteLine("DisposeByTimer invoked for {0}", record.TransmissionId);
      if (record.AsyncResult != null)
      {
        record.AsyncResult.SetAsCompleted(false);
      }
      record.Dispose();
    }
  }

  /// <summary>
  /// Implements the beginning of data transmission
  /// </summary>
  internal class FirstPacketSending : ReliableUdpState
  {
    public override void SendPacket(ReliableUdpConnectionRecord connectionRecord)
    {
      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);

        connectionRecord.PacketCounter = 0;
        connectionRecord.SndNext = 0;
        connectionRecord.WindowLowerBound = 0;

        #region Sending a message that does not require confirmation of delivery (Flags = NoAsk)

        //RU: если подтверждения не требуется - отправляем все пакеты
        //RU: и высвобождаем ресурсы
        //EN: if confirmation of delivery doesn't requires than
        //EN: send all packets and release the resources
        if (connectionRecord.IsNoAnswerNeeded)
        {
          //RU: Здесь происходит отправка As Is
          //RU: при такой отправке возможен вариант 
          //RU: кроме того при большой нагрузке возможна потеря первого пакета (не попадание в буфер приема)
          //RU: соответственно в таком случае все отправленные с данной TransmissionId 
          //RU: пакеты буду отброшены  
          //EN: Here is "As Is" sending
          //EN: If in some cases first packet won't arrive on endpoint than all packets of the
          //EN: current TransmissionId will be droped
          do
          {
            connectionRecord.CToken.ThrowIfCancellationRequested();
            ReliableUdpStateTools.SendPacket(connectionRecord,
                                             ReliableUdpStateTools.CreateUdpPayload(connectionRecord,
                                                                                    ReliableUdpStateTools.
                                                                                      CreateReliableUdpHeader(
                                                                                        connectionRecord)
                                               ));
            connectionRecord.SndNext++;
          } while (connectionRecord.SndNext < connectionRecord.NumberOfPackets);
          SetAsCompleted(connectionRecord);
          return;
        }

        #endregion

        connectionRecord.CToken.ThrowIfCancellationRequested();
        ReliableUdpHeader header = ReliableUdpStateTools.CreateReliableUdpHeader(connectionRecord);
        ReliableUdpStateTools.SendPacket(connectionRecord,
                                         ReliableUdpStateTools.CreateUdpPayload(connectionRecord, header));

        //RU: увеличиваем счетчик
        //EN: increase SndNext
        connectionRecord.SndNext++;
        //RU: сдвигаем окно
        //EN: move window lower bound
        connectionRecord.WindowLowerBound++;
        connectionRecord.State = connectionRecord.Tcb.States.SendingCycle;
        //RU: Запускаем таймер
        //EN: Start the timer
        connectionRecord.WaitForPacketsTimer = new Timer(CheckByTimer, connectionRecord,
                                                         connectionRecord.ShortTimerPeriod, -1);
      }
      catch (ArgumentNullException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentOutOfRangeException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ObjectDisposedException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (InvalidOperationException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (System.Net.Sockets.SocketException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch( OperationCanceledException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    public override string ToString()
    {
      return "State: FirstPacketSending";
    }
  }

  /// <summary>
  /// Implements data transmission and completion of data transmission 
  /// </summary>
  internal class SendingCycle : ReliableUdpState
  {
    public override void ReceivePacket(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header,
                                       byte[] payload)
    {
      try
      {
        //RU: ПРИМЕЧАНИЯ:
        //RU: важно понимать разницу между подтверждениями о получении 
        //RU: и запросами потерянных пакетов.
        //RU: когда отправляем подтверждение, то указываем номер последовательности,
        //RU: которая должна прийти следующей
        //RU: когда отправляем запрос о получении потерянных пакетов
        //RU: то указываем номер той конкретной последовательности которая потерялась     
        //EN: NOTES:
        //EN: it is important to understand the difference between the acknowledgment
        //EN: of the receipt of packets and requests for lost packets.
        //EN: When we send acknowledge we set the next expected packet number
        //EN: When we send request for lost packet we set concrete packet number of lost packet
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);

        //EN: if cancellation requested - stop and release resources
        connectionRecord.CToken.ThrowIfCancellationRequested();

        if (connectionRecord.IsDone != 0)
          return;

        if (!header.Flags.HasFlag(ReliableUdpHeaderFlags.RequestForPacket))
          //EN: drop packet
          return;

        //RU: расчет конечной границы окна, чтобы избежать ошибок с записью/чтением в несуществующий индекс
        //RU: в расчете берется граница окна + 1 - для получения подтверждений 
        //EN: calculation of the final border of the window. 
        int windowHighestBound = Math.Min((connectionRecord.WindowLowerBound + connectionRecord.WindowSize),
                                          (connectionRecord.NumberOfPackets));

        //RU: проверка на попадание в окно
        //EN: check to get into the reception window
        if (header.PacketNumber < connectionRecord.WindowLowerBound || header.PacketNumber > windowHighestBound)
          //EN: drop packet
          return;

        connectionRecord.WaitForPacketsTimer.Change(connectionRecord.ShortTimerPeriod, -1);
        if (connectionRecord.CloseWaitTimer != null)
        {
          connectionRecord.CloseWaitTimer.Change(-1, -1);
        }

        //RU: проверить на последний пакет:
        //EN: check for last packet
        if (header.PacketNumber == connectionRecord.NumberOfPackets)
        {
          //RU: передача завершена
          //EN: transfer completed
          Debug.WriteLine("Done {0}! ", connectionRecord.TransmissionId);
          Interlocked.Increment(ref connectionRecord.IsDone);
          SetAsCompleted(connectionRecord);
          return;
        }

        //RU: это ответ на первый пакет c подтверждением 
        //EN: check for acknowledge of the first packet
        if ((header.Flags.HasFlag(ReliableUdpHeaderFlags.FirstPacket) && header.PacketNumber == 1))
        {
          //RU: без сдвига окна
          //EN: don't move window lower bound
          SendPacket(connectionRecord);
        }
          //RU: пришло подтверждение о получении блока данных
          //EN: check confirmation of delivery of block of packets 
        else if (header.PacketNumber == windowHighestBound)
        {
          //Debug.WriteLine( "#{0} ask received", Header.PacketNumber );
          //RU: сдвигаем окно
          //EN: move window
          connectionRecord.WindowLowerBound += connectionRecord.WindowSize;
          //RU: обнуляем массив контроля передачи
          //EN: nullify window control array
          connectionRecord.WindowControlArray.Nullify();
          SendPacket(connectionRecord);
        }
          //RU: это запрос на повторную передачу
          //EN: if we are here than it is request for retransmission
        else
          ReliableUdpStateTools.SendPacket(connectionRecord,
                                           ReliableUdpStateTools.RetransmissionCreateUdpPayload(connectionRecord,
                                                                                                header.PacketNumber));
      }
      catch (ArgumentNullException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentOutOfRangeException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ObjectDisposedException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (InvalidOperationException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (System.Net.Sockets.SocketException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch(OperationCanceledException ex)
      {
        SetAsError( connectionRecord, ex );
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    public override void SendPacket(ReliableUdpConnectionRecord connectionRecord)
    {
      //Debug.WriteLine( "#{0} block sending...", connectionRecord.SndNext );
      //RU: отправляем блок пакетов
      //EN: sending block of packets
      for (connectionRecord.PacketCounter = 0;
           connectionRecord.PacketCounter < connectionRecord.WindowSize &&
           connectionRecord.SndNext < connectionRecord.NumberOfPackets;
           connectionRecord.PacketCounter++)
      {
        ReliableUdpHeader header = ReliableUdpStateTools.CreateReliableUdpHeader(connectionRecord);
        ReliableUdpStateTools.SendPacket(connectionRecord,
                                         ReliableUdpStateTools.CreateUdpPayload(connectionRecord, header));
        connectionRecord.SndNext++;
      }
      //RU: на случай большого окна передачи, перезапускаем таймер после отправки
      connectionRecord.WaitForPacketsTimer.Change( connectionRecord.ShortTimerPeriod, -1 );
      if ( connectionRecord.CloseWaitTimer != null )
      {
        connectionRecord.CloseWaitTimer.Change( -1, -1 );
      }
    }

    /// <summary>
    /// Invokes on timer
    /// </summary>
    /// <param name="connectionRecord"></param>
    /// <returns></returns>
    public override void ProcessPackets(ReliableUdpConnectionRecord connectionRecord)
    {
      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);
        if (connectionRecord.IsDone != 0)
          return;
        //Debug.WriteLine( "In timer. Means no ask received" );
        //RU: отправляем повторно последний пакет 
        //RU: ( в случае восстановления соединения узел-приемник заново отправит запросы, которые до него не дошли)
        //EN: send the last sent message
        //EN: if connection will be established again the recepient will send again all request for lost packets
        ReliableUdpStateTools.SendPacket(connectionRecord,
                                         ReliableUdpStateTools.RetransmissionCreateUdpPayload(connectionRecord,
                                                                                              connectionRecord.SndNext -
                                                                                              1));

        //RU: включаем таймер CloseWait
        //EN: start closewait timer
        StartCloseWaitTimer(connectionRecord);
      }
      catch (ArgumentNullException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentOutOfRangeException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ObjectDisposedException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (InvalidOperationException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (System.Net.Sockets.SocketException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    public override string ToString()
    {
      return "State: SendingCycle";
    }
  }

  /// <summary>
  /// Implements the beginning of reception of data
  /// </summary>
  internal class FirstPacketReceived : ReliableUdpState
  {
    public override void ReceivePacket(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header,
                                       byte[] payload)
    {
      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);
        //RU: Шаг1. первым делом, при получении пакета с флагом FirstMessage
        //RU: мы должны создать в структуре TCB новый поток в памяти,
        //RU: для записи туда полученных байт.
        //RU: Шаг.2 увеличить счетчик принятых пакетов, в дальнейшем
        //RU: он нам понадобится для расчета верности полученных данных
        //RU: Шаг 3.Записать данные из полученного пакета в memoryStream
        //RU: Шаг 4.Отправить пакет подтверждение
        //RU: Шаг 5.Включить таймер ожидания пакетов, при срабатывании которого
        //RU: переходим в состоянии длительного ожидания пакетов ( WaitForPackets )

        if (!header.Flags.HasFlag(ReliableUdpHeaderFlags.FirstPacket))
          //EN: drop this packet;
          return;
        //RU: комбинация двух флагов - FirstPacket и LastPacket - говорит что у нас единственное сообщение
        //EN: combination of flags FirstPacket and LastPacket means that we received a single packet message 
        if (header.Flags.HasFlag(ReliableUdpHeaderFlags.FirstPacket) &
            header.Flags.HasFlag(ReliableUdpHeaderFlags.LastPacket))
        {
          ReliableUdpStateTools.CreateMessageFromSinglePacket(connectionRecord, header,
                                                              payload.Slice(ReliableUdpHeader.Length, payload.Length));
          //RU: отправляем пакет подтверждение
          //EN: send acknowledge packet
          if (!header.Flags.HasFlag(ReliableUdpHeaderFlags.NoAsk))
          {
            ReliableUdpStateTools.SendAcknowledgePacket(connectionRecord);
          }
          SetAsCompleted(connectionRecord);
          Debug.WriteLine("Done!");
          return;
        }

        //RU: поминим, что by design все packet numbers начинаются с 0;
        //EN: all packet numbers of any transmission start from 0 by design
        if (header.PacketNumber != 0)
          //EN: drop this packet;
          return;

        ReliableUdpStateTools.InitIncomingBytesStorage(connectionRecord, header);
        ReliableUdpStateTools.WritePacketData(connectionRecord, header, payload);

        //RU: считаем кол-во пакетов, которые должны прийти
        connectionRecord.NumberOfPackets =
          (int)
          Math.Ceiling((double) ((double) connectionRecord.IncomingStream.Length/(double) connectionRecord.BufferSize));

        //RU: записываем номер последнего полученного пакета (0)
        connectionRecord.RcvCurrent = header.PacketNumber;

        //RU: после сдвинули окно приема на 1
        //EN: shift window by 1
        connectionRecord.WindowLowerBound++;

        connectionRecord.State = connectionRecord.Tcb.States.Assembling;

        //RU: если не требуется подтверждение
        //RU: запускаем таймер который высвободит все структуры 
        //EN: if we have NoAsk transmission
        //EN: Start a dispose timer
        if (header.Flags.HasFlag(ReliableUdpHeaderFlags.NoAsk))
        {
          connectionRecord.CloseWaitTimer = new Timer(DisposeByTimeout, connectionRecord,
                                                      connectionRecord.ShortTimerPeriod, -1);
        }
        else
        {
          ReliableUdpStateTools.SendAcknowledgePacket(connectionRecord);
          //EN: Start WaitForPacketsTimer
          connectionRecord.WaitForPacketsTimer = new Timer(CheckByTimer, connectionRecord,
                                                           connectionRecord.ShortTimerPeriod, -1);
        }
      }
      catch (ArgumentNullException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentOutOfRangeException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ObjectDisposedException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (InvalidOperationException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (System.Net.Sockets.SocketException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    public override string ToString()
    {
      return "State: FirstPacketReceived";
    }
  }

  /// <summary>
  /// Implements reception of data
  /// </summary>
  internal class Assembling : ReliableUdpState
  {
    public override void ReceivePacket(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header,
                                       byte[] payload)
    {
      #region Some comments

      //RU: ПРИМЕЧАНИЯ:
      //RU: важно понимать разницу между подтверждениями о получении 
      //RU: и запросами потерянных пакетов.
      //RU: когда отправляем подтверждение, то указываем номер последовательности,
      //RU: которая должна прийти следующей
      //RU: когда отправляем запрос о получении потерянных пакетов
      //RU: то указываем номер той конкретной последовательности которая потерялась     
      //EN: NOTES:
      //EN: it is important to understand the difference between the acknowledgment
      //EN: of the receipt of packets and requests for lost packets.
      //EN: When we send acknowledge we set the next expected packet number
      //EN: When we send request for lost packet we set concrete packet number of lost packet


      //RU: Основной класс собирающий пакеты
      //RU: Шаг 1. Записываем пакет в память
      //RU: Шаг 2. Проверяем счетчик пакетов:
      //RU: Если счетчик пакетов достиг значения RcvPacketsMax 
      //RU: и если проверка пакетов завершилась успешно:
      //RU: отправляем подтверждение о получении
      //RU: иначе: запускаем процедуру запроса пакетов

      //RU: примеры
      //EN: Examples
      // S(Sender), R(Receiver), udp1 (udp packet, where 1 - seq.number), L - Flags.LastMessage, ReceiverBuffer equals 5
      //
      // Good transmission, without errors:
      // S --> udp6 -->R
      // S --> udp7 -->R
      // S --> udp8 -->R
      // S --> udp9 -->R
      // S --> udp10L -->R
      // on Receiver (we just get udp10L packet):
      // check if  Header.PacketNumber > connectionRecord.RcvWindowStartPacket (true)
      // check if we have no such record in our RcvWindowControlArray (true)
      // check if Flags has LastMessage. (true)
      // check if PacketCounter == RecieverBuffer to perform checking for packets loss and dublicates (true - no errors)
      // if true && true && true && true => go to the state Completed and RunItsOwnChecking
      //
      // Transmission with duplicates, without errors:
      // S --> udp6 -->R
      // S --> udp7 -->R
      // S --> udp8 -->R
      // S --> udp8 -->R      
      // on Receiver (we just get duplicate of udp8 packet):
      // check if  Header.PacketNumber > connectionRecord.RcvWindowStartPacket (true)
      // check if we have no such record in our RcvWindowControlArray (false - means we received this packet before)
      //  -> Decrement PacketCounter ( because it was incremented in WritePacketData )
      //  -> get away this packet
      //
      // Transmission with duplicates, without errors:
      // S --> udp6 -->R
      // S --> udp7 -->R
      // S --> udp8 -->R
      // S --> udp5 -->R      
      // on Receiver (we just get duplicate of udp5 packet, who has been received in previous checking session):
      // check if  Header.PacketNumber > connectionRecord.RcvWindowStartPacket (false)
      //  -> Decrement PacketCounter ( because it was incremented in WritePacketData )
      //  -> get away this packet 

      #endregion

      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);
        if (connectionRecord.IsDone != 0)
          return;

        #region Processing packets with NoAsk flag

        if (header.Flags.HasFlag(ReliableUdpHeaderFlags.NoAsk))
        {
          //RU: сбрасываем таймер
          connectionRecord.CloseWaitTimer.Change(connectionRecord.LongTimerPeriod, -1);
          //EN: Write data to byte array. 
          ReliableUdpStateTools.WritePacketData(connectionRecord, header, payload);
          //RU: если получили пакет с последним флагом - завершаем
          //EN: if we received packet with LastPacket flag - go to Completed state
          if (header.Flags.HasFlag(ReliableUdpHeaderFlags.LastPacket))
          {
            connectionRecord.State = connectionRecord.Tcb.States.Completed;
            connectionRecord.State.ProcessPackets(connectionRecord);
          }
          return;
        }

        #endregion

        //RU: расчет конечной границы окна, чтобы избежать ошибок с записью/чтением в несуществующий индекс
        int windowHighestBound = Math.Min((connectionRecord.WindowLowerBound + connectionRecord.WindowSize - 1),
                                          (connectionRecord.NumberOfPackets - 1));
        //EN: drop this packet if true. It is not in receive window
        if (header.PacketNumber < connectionRecord.WindowLowerBound ||
            header.PacketNumber > (windowHighestBound))
          return;

        //EN: drop this packet if true. It is duplicate
        if (connectionRecord.WindowControlArray.Contains(header.PacketNumber))
          return;

        //EN: Write data to stream. 
        ReliableUdpStateTools.WritePacketData(connectionRecord, header, payload);
        //RU: увеличиваем счетчик пакетов
        //EN: increase packet counter
        connectionRecord.PacketCounter++;

        //RU: записываем в массив управления окном текущий номер пакета
        //EN: write to control array current packet number
        connectionRecord.WindowControlArray[header.PacketNumber - connectionRecord.WindowLowerBound] =
          header.PacketNumber;

        //RU: устанавливаем наибольший пришедший пакет
        //EN: set the maximum incoming packet
        if (header.PacketNumber > connectionRecord.RcvCurrent)
          connectionRecord.RcvCurrent = header.PacketNumber;

        //RU: перезапуск таймеров        
        connectionRecord.TimerSecondTry = false;
        connectionRecord.WaitForPacketsTimer.Change(connectionRecord.ShortTimerPeriod, -1);
        if (connectionRecord.CloseWaitTimer != null)
        {
          connectionRecord.CloseWaitTimer.Change(-1, -1);
        }

        //RU: устанавливаем IsLastPacketReceived - что пришел последний пакет 
        //EN: if last packet has arrived than increase IsLastPacketReceived
        if (header.Flags.HasFlag(ReliableUdpHeaderFlags.LastPacket))
        {
          Interlocked.Increment(ref connectionRecord.IsLastPacketReceived);
        }
          //RU: если нам пришли все пакеты окна, то сбрасываем счетчик
          //RU: и высылаем пакет подтверждение
          //EN: if we received all packets for the current window
          //EN: reset the counter and send acknowledge packet
        else if (connectionRecord.PacketCounter == connectionRecord.WindowSize)
        {
          //RU: сбрасываем счетчик.      
          connectionRecord.PacketCounter = 0;
          //RU: сдвинули окно передачи
          //EN: move window
          connectionRecord.WindowLowerBound += connectionRecord.WindowSize;
          //RU: обнуление массива управления передачей
          connectionRecord.WindowControlArray.Nullify();
          ReliableUdpStateTools.SendAcknowledgePacket(connectionRecord);
        }

        //RU: если последний пакет уже имеется
        //EN: if we received last packet
        if (Thread.VolatileRead(ref connectionRecord.IsLastPacketReceived) != 0)
        {
          //RU: проверяем пакеты
          //EN: check the packets
          ProcessPackets(connectionRecord);
        }
      }
      catch (ArgumentNullException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentOutOfRangeException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ObjectDisposedException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (InvalidOperationException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (System.Net.Sockets.SocketException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    public override void ProcessPackets(ReliableUdpConnectionRecord connectionRecord)
    {
      #region Some comments

      //RU: В этот метод мы можем попасть в трех случаях
      //RU: Первый:
      //RU: у нас пришел последний пакет и нам необходимо проверить 
      //RU: остались ли у нас какие-либо не пришедшие пакеты.
      //RU: если все пришли - переключаемся на сборку сообщения
      //RU: если есть потери - составляем список потерянных пакетов
      //RU: Второй случай:
      //RU: сработал таймер - а следовательно у нас какие-то пакеты
      //RU: не пришли вовремя. Составляем список потерянных пакетов
      //RU: Третий случай - сработал таймер, все пакеты пришли вовремя
      //RU: но ack пакет на блок данных не дошел до отправителя
      //EN: We can get into this method in three cases
      //EN: First case:
      //EN: we received last packet and we have to test for lost packets
      //EN: Second case:
      //EN: WaitForPacketsTimer fired and we have some lost packets
      //EN: Third case:
      //EN: WaitForPacketsTimer fired, acknowledge packet lost

      #endregion

      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);
        if (connectionRecord.IsDone != 0)
          return;
        //Debug.WriteLine( "In receiver checking" );
        if (!ReliableUdpStateTools.CheckForNoPacketLoss(connectionRecord, connectionRecord.IsLastPacketReceived != 0))
        {
          //RU: есть потерянные пакеты, отсылаем запросы на них
          //EN: we have lost packets. Send request for each
          foreach (int seqNum in connectionRecord.LostPackets)
          {
            if (seqNum != 0)
            {
              //Debug.WriteLine("SENDING REQUEST TO {0} FOR {1}\nWindowLowerBound {2}, RcvCurrent {3}\nNumberOfPackets{4}",
              //  connectionRecord.TransmissionId, seqNum, connectionRecord.WindowLowerBound, connectionRecord.RcvCurrent, connectionRecord.NumberOfPackets );
              ReliableUdpStateTools.SendAskForLostPacket(connectionRecord, seqNum);
            }
          }
          //RU: устанавливаем таймер во второй раз, для повторной попытки передачи
          if (!connectionRecord.TimerSecondTry)
          {
            connectionRecord.WaitForPacketsTimer.Change(connectionRecord.ShortTimerPeriod, -1);
            connectionRecord.TimerSecondTry = true;
            return;
          }
          //RU: если после двух попыток срабатываний WaitForPacketTimer 
          //RU: не удалось получить пакеты - запускаем таймер завершения соединения
          StartCloseWaitTimer(connectionRecord);
        }
        else if (connectionRecord.IsLastPacketReceived != 0)
          //RU: успешная проверка 
          //EN: successful check
        {
          //RU: высылаем подтверждение о получении данных окна
          //EN: send last ask
          //Debug.WriteLine("Sending last ask for {0}", connectionRecord.TransmissionId);
          ReliableUdpStateTools.SendAcknowledgePacket(connectionRecord);
          connectionRecord.State = connectionRecord.Tcb.States.Completed;
          connectionRecord.State.ProcessPackets(connectionRecord);
          //RU: вместо моментальной реализации ресурсов
          //RU: запускаем таймер, на случай, если
          //RU: если последний ack не дойдет до отправителя и он запросит его снова.
          //RU: по срабатыванию таймера - реализуем ресурсы
          //RU: !!в состоянии Completed метод таймера переопределен
          StartCloseWaitTimer(connectionRecord);
        }
          //EN: in this case we have lost acknowledge packet
          //RU: это случай, когда ack на блок пакетов был потерян
        else
        { 
          if (!connectionRecord.TimerSecondTry)
          {
            ReliableUdpStateTools.SendAcknowledgePacket(connectionRecord);
            connectionRecord.WaitForPacketsTimer.Change(connectionRecord.ShortTimerPeriod, -1);
            connectionRecord.TimerSecondTry = true;
            return;
          }
          //RU: не удалось получить пакеты - запускаем таймер завершения соединения
          StartCloseWaitTimer(connectionRecord);
        }
      }
      catch (ArgumentNullException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentOutOfRangeException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ArgumentException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (ObjectDisposedException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (InvalidOperationException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      catch (System.Net.Sockets.SocketException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }


    public override string ToString()
    {
      return "State: Assembling";
    }
  }

  /// <summary>
  /// Implements completion of reception of data
  /// </summary>
  internal class Completed : ReliableUdpState
  {
    public override void ReceivePacket(ReliableUdpConnectionRecord connectionRecord, ReliableUdpHeader header,
                                       byte[] payload)
    {
      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);
        //RU: сюда могут попасть либо задержавшиеся дубликаты
        //RU: либо повторная отправка последнего пакета в связи с тем,
        //RU: что последний ack не дошел до отправителя
        if (header.Flags.HasFlag(ReliableUdpHeaderFlags.LastPacket))
        {
          ReliableUdpStateTools.SendAcknowledgePacket(connectionRecord);
        }
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    public override void ProcessPackets(ReliableUdpConnectionRecord connectionRecord)
    {
      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);
        //RU: Переход в этот метод всегда инициируется из состояний Assembling
        //RU: при прохождении проверки и получении пакета с флагом LastMessage.
        //EN: Transition to this method is always initiated from Assembling state
        //EN: when checking is passed successfully and packet with LastMessage flag
        //EN: received
        Interlocked.Increment(ref connectionRecord.IsDone);

        //RU: т.к. для избежания случая недостави последнего подтверждения мы 
        //RU: реализуем ресурсы по срабатыванию Dispose таймера, то
        //RU: здесь мы лишь останавливаем WaitForPackets таймер

        if (connectionRecord.WaitForPacketsTimer != null)
          connectionRecord.WaitForPacketsTimer.Dispose();
        //RU: и передаем сообщение подписчикам
        ReliableUdpStateTools.CreateMessageFromMemoryStream(connectionRecord);
      }
      catch (ObjectDisposedException ex)
      {
        SetAsError(connectionRecord, ex);
      }
      finally
      {
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    //RU: реализуем ресурсы после ожидания (на случай потери последнего ack)
    protected override void DisposeByTimeout(object record)
    {
      ReliableUdpConnectionRecord connectionRecord = (ReliableUdpConnectionRecord) record;
      try
      {
        System.Threading.Monitor.Enter(connectionRecord.LockerReceive);
      }
      finally
      {
        SetAsCompleted(connectionRecord);
        System.Threading.Monitor.Exit(connectionRecord.LockerReceive);
      }
    }

    public override string ToString()
    {
      return "State: Completed";
    }
  }
}