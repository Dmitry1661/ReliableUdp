using System;

namespace Lihtarovich.ReliableUdp.Core
{
  /// <summary>
  /// Provides extension methods for Array class
  /// </summary>
  internal static class ArrayExtensions
  {
    /// <summary>
    /// returns part of the array since Start, till End
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="start">index of start element</param>
    /// <param name="end">index of end element</param>
    /// <returns></returns>
    public static T[] Slice<T>(this T[] source,
                               int start,
                               int end)
    {
      if (null == source)
        throw new ArgumentNullException("source");

      if (end < start)
        throw new ArgumentException("End<Start");

      if (end < 0)
        end = source.Length - 1;

      int len = end - start;


      T[] res = new T[len];
      Array.Copy(source, start, res, 0, len);
      return res;
    }

    /// <summary>
    /// Sets default values for the type of data
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    public static void Nullify<T>(this T[] source)
    {
      for (int i = 0; i < source.Length; i++)
      {
        source[i] = default(T);
      }
    }
  }
}