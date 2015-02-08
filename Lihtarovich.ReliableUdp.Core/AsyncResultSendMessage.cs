// ******************************************************************************
// Module:  AsyncResultSendMessage.cs
// Notices: My realization of APM. Based on following articles:
// https://msdn.microsoft.com/en-us/magazine/cc163467.aspx by Jeffrey Richter
// http://blogs.msdn.com/b/nikos/archive/2011/03/14/how-to-implement-iasyncresult-in-another-way.aspx  
// ******************************************************************************

using System;
using System.Net;
using System.Threading;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Encapsulates the results of an asynchronous operation on BeginSend/EndSend methods of <see cref="ReliableUdpConnectionControlBlock"/>
  /// </summary>
  internal class AsyncResultSendMessage : AsyncResult<Boolean>
  {
    private readonly ReliableUdpMessage m_reliableUdpMessage;

    /// <summary>
    /// Gets <see cref="ReliableUdpMessage"/>
    /// </summary>
    public ReliableUdpMessage ReliableUdpMessage
    {
      get { return m_reliableUdpMessage; }
    }

    private readonly EndPoint m_remoteEndPoint;

    /// <summary>
    /// Gets remote endpoint
    /// </summary>
    public EndPoint EndPoint
    {
      get { return m_remoteEndPoint; }
    }

    /// <summary>
    /// Initializes a new instance of the AsyncResultSendMessage 
    /// </summary>
    /// <param name="reliableUdpMessage">Message to send</param>
    /// <param name="endPoint">Remote endpoint</param>
    /// <param name="asyncCallback">asyncCallback method</param>
    /// <param name="state">An object that contains state information for this request</param>
    /// <param name="owner">Object who calls the BeginSend method (instance of <see cref="ReliableUdpConnectionControlBlock"/>)</param>
    public AsyncResultSendMessage(ReliableUdpMessage reliableUdpMessage, EndPoint endPoint,
                                  AsyncCallback asyncCallback, object state, object owner)
      : base(asyncCallback, state, owner)
    {
      m_reliableUdpMessage = reliableUdpMessage;
      m_remoteEndPoint = endPoint;
    }
  }

  internal partial class AsyncResult<TResult> : AsyncResultNoResult
  {
    private TResult m_result = default(TResult);

    protected void SetResult(TResult result)
    {
      m_result = result;
    }

    public AsyncResult(AsyncCallback asyncCallback, object state,
                       object owner) : base(asyncCallback, state, owner)
    {
    }

    public void SetAsCompleted(TResult result, Boolean completedSynchronously = false)
    {
      // Save the asynchronous operation's result
      m_result = result;

      // Tell the base class that the operation completed sucessfully (no exception)
      base.SetAsCompleted(null, completedSynchronously);
    }

    public new static TResult EndInvoke(IAsyncResult asyncResult, object owner)
    {
      AsyncResult<TResult> ar = asyncResult as AsyncResult<TResult>;
      if (ar == null)
      {
        throw new ArgumentException("Result passed represents an operation not supported " +
                                    "by this framework.");
      }
      AsyncResultNoResult.EndInvoke(asyncResult, owner);
      return ar.m_result;
    }
  }

  internal partial class AsyncResultNoResult : IAsyncResult
  {
    // Fields set at construction which never change while
    // operation is pending
    private readonly AsyncCallback m_asyncCallback;
    private readonly Object m_asyncState;

    // Fields set at construction which do change after
    // operation completes
    private const Int32 c_StatePending = 0;
    private const Int32 c_StateCompletedSynchronously = 1;
    private const Int32 c_StateCompletedAsynchronously = 2;
    private Int32 m_completedState = c_StatePending;

    // Field that may or may not get set depending on usage
    private ManualResetEvent m_asyncWaitHandle;

    // Fields set when operation completes
    private Exception m_exception;

    /// <summary>
    /// The object which started the operation.
    /// </summary>
    private readonly object m_owner;

    public AsyncResultNoResult(AsyncCallback asyncCallback, object state,
                               object owner)
    {
      m_asyncCallback = asyncCallback;
      m_asyncState = state;
      m_owner = owner;
    }

    public void SetAsCompleted(Exception exception, Boolean completedSynchronously = false)
    {
      // Passing null for exception means no error occurred; this is the common case
      m_exception = exception;

      // The m_CompletedState field MUST be set prior calling the callback
      Int32 prevState = Interlocked.Exchange(ref m_completedState,
                                             completedSynchronously
                                               ? c_StateCompletedSynchronously
                                               : c_StateCompletedAsynchronously);
      if (prevState != c_StatePending)
        throw new InvalidOperationException("You can set a result only once");

      // Do any processing before completion.
      this.Completing(exception, completedSynchronously);

      // If the event exists, set it
      if (m_asyncWaitHandle != null) m_asyncWaitHandle.Set();

      // If a callback method was set, call it
      this.MakeCallback(m_asyncCallback, this);

      // Do any final processing after completion
      this.Completed(exception, completedSynchronously);
    }

    public static void EndInvoke(IAsyncResult result, object owner)
    {
      AsyncResultNoResult ar = result as AsyncResultNoResult;
      if (ar == null)
      {
        throw new ArgumentException("Result passed represents an operation not supported " +
                                    "by this framework.");
      }

      ar.CheckUsage(owner);

      // This method assumes that only 1 thread calls EndInvoke for this object
      if (!ar.IsCompleted)
      {
        // If the operation isn't done, wait for it
        ar.AsyncWaitHandle.WaitOne();
        ar.AsyncWaitHandle.Close();
        ar.m_asyncWaitHandle = null; // Allow early GC
      }
      // Operation is done: if an exception occured, throw it
      if (ar.m_exception != null) throw ar.m_exception;
    }

    private void CheckUsage(object owner)
    {
      if (!object.ReferenceEquals(owner, m_owner))
      {
        throw new InvalidOperationException(
          "End was called on a different object than Begin.");
      }
    }

    #region Implementation of IAsyncResult

    public Object AsyncState
    {
      get { return m_asyncState; }
    }

    public Boolean CompletedSynchronously
    {
      get { return Thread.VolatileRead(ref m_completedState) == c_StateCompletedSynchronously; }
    }

    public WaitHandle AsyncWaitHandle
    {
      get
      {
        if (m_asyncWaitHandle == null)
        {
          Boolean done = IsCompleted;
          ManualResetEvent mre = new ManualResetEvent(done);
          if (Interlocked.CompareExchange(ref m_asyncWaitHandle, mre, null) != null)
          {
            // Another thread created this object's event; dispose the event we just created
            mre.Close();
          }
          else
          {
            if (!done && IsCompleted)
            {
              // If the operation wasn't done when we created 
              // the event but now it is done, set the event
              m_asyncWaitHandle.Set();
            }
          }
        }
        return m_asyncWaitHandle;
      }
    }

    public Boolean IsCompleted
    {
      get { return Thread.VolatileRead(ref m_completedState) != c_StatePending; }
    }

    #endregion

    #region Extensibility

    protected virtual void Completing(
      Exception exception, bool completedSynchronously)
    {
    }

    protected virtual void MakeCallback(AsyncCallback callback, AsyncResultNoResult result)
    {
      // If a callback method was set, call it
      if (callback != null)
      {
        callback(result);
      }
    }

    protected virtual void Completed(Exception exception, bool completedSynchronously)
    {
    }

    #endregion
  }
}