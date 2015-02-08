using System;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// The exception that is thrown when invalid settings passed to ReliableUdp core 
  /// </summary>
  [Serializable]
  public class ReliableUdpConfigurationException : Exception
  {
    /// <summary>
    /// Initializes a new instance of the ReliableUdpConfigurationException class.
    /// </summary>
    public ReliableUdpConfigurationException() { }
    /// <summary>
    /// Initializes a new instance of the ReliableUdpConfigurationException class with a specified error message.
    /// </summary>
    /// <param name="message">Description of error</param>
    public ReliableUdpConfigurationException( string message ) : base( message ) { }
    /// <summary>
    /// Initializes a new instance of the ReliableUdpConfigurationException class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">Description of error</param>
    /// <param name="inner">Inner exception</param>
    public ReliableUdpConfigurationException( string message, Exception inner ) : base( message, inner ) { }
    /// <summary>
    /// Initializes a new instance of the ReliableUdpConfigurationException class with serialized data.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    protected ReliableUdpConfigurationException(
    System.Runtime.Serialization.SerializationInfo info,
    System.Runtime.Serialization.StreamingContext context )
      : base( info, context ) { }
  }
}
