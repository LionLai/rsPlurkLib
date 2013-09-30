using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Specialized;
using Newtonsoft.Json;

namespace rsPlurkLib
{
    /// <summary>
    /// Contains helper HTTP methods not available in System.Net namespace.
    /// </summary>
    public static class HttpUtility
    {
        #region "Public Methods"

        /// <summary>
        /// Parse an encoded query string using UTF8 encoding.
        /// </summary>
        /// <param name="s">The query string to parse.</param>
        public static T ParseQueryString<T>(String s)
        {
            return ParseQueryString<T>(s, true, Encoding.UTF8);
        }

        /// <summary>
        /// Parse a query string into a NameValueCollection using specified encoding settings.
        /// </summary>
        /// <param name="s">The query string to parse.</param>
        /// <param name="urlencoded">Whether query string keys and values are URL encoded.</param>
        /// <param name="encoding">The encoding to use.</param>
        public static T ParseQueryString<T>(String s, bool urlencoded, Encoding encoding)
        {
            T result = JsonConvert.DeserializeObject<T>(s);
            return result;
        }

        /// <summary>
        /// Decode an URL transmission-encoded string into its original representation using UTF8 encoding.
        /// </summary>
        /// <param name="s">The string to decode.</param>
        public static string UrlDecode(string s)
        {
            return UrlDecode(s, Encoding.UTF8);
        }

        /// <summary>
        /// Decode an URL transmission-encoded string into its original representation using specified encoding.
        /// </summary>
        /// <param name="s">The string to decode.</param>
        /// <param name="encoding">The encoding to use.</param>
        public static string UrlDecode(string s, Encoding encoding)
        {
            if (s == null) return null;

            int count = s.Length;
            UrlDecoder buffer = new UrlDecoder(count, encoding);

            for (int pos = 0; pos < count; pos++) {
                char ch = s[pos];

                if (ch == '+') ch = ' ';
                else
                    if ((ch == '%') && (pos < count - 2)) {
                        if ((s[pos + 1] == 'u') && pos < (count - 5)) { // %uXXXX
                            int h1 = HexToInt(s[pos + 2]);
                            int h2 = HexToInt(s[pos + 3]);
                            int h3 = HexToInt(s[pos + 4]);
                            int h4 = HexToInt(s[pos + 5]);

                            if (h1 >= 0 && h2 >= 0 && h3 >= 0 && h4 >= 0) {
                                ch = (char)((h1 << 12) | (h2 << 8) | (h3 << 4) | h4);
                                pos += 5;
                                buffer.AddChar(ch); continue;
                        }
                    }
                    else {  // %XX
                        int h1 = HexToInt(s[pos + 1]);
                        int h2 = HexToInt(s[pos + 2]);

                        if (h1 >= 0 && h2 >= 0) {
                            byte b = (byte)((h1 << 4) | h2);
                            pos += 2;
                            buffer.AddByte(b); continue;
                        }
                    }
                }

                if ((ch & 0xFF80) == 0)
                    buffer.AddByte((byte)ch);
                else
                    buffer.AddChar(ch);
            }

            return buffer.GetString();
        }

        #endregion

        #region "Private Helpers"

        /// <summary>
        /// Convert a hex character to its integer representation,
        /// </summary>
        private static int HexToInt(char h)
        {
            return (h >= '0' && h <= '9') ? h - '0' :
            (h >= 'a' && h <= 'f') ? h - 'a' + 10 :
            (h >= 'A' && h <= 'F') ? h - 'A' + 10 :
            -1;
        }

        /// <summary>
        /// Helper class to queue characters and buffers to be encoded.
        /// </summary>
        private class UrlDecoder
        {
            private int _bufferSize;

            private int _charCount;
            private char[] _charBuffer;

            private int _byteCount;
            private byte[] _byteBuffer;

            private Encoding _encoding;

            internal UrlDecoder(int bufferSize, Encoding encoding)
            {
                _bufferSize = bufferSize;
                _encoding = encoding;
                _charBuffer = new char[bufferSize];
                // _byteByffer creates on demand
            }

            internal void AddChar(char ch)
            {
                if (_byteCount > 0) FlushBytes();
                _charBuffer[_charCount++] = ch;
            }

            internal void AddByte(byte b)
            {
                if (_byteBuffer == null) _byteBuffer = new byte[_bufferSize];
                _byteBuffer[_byteCount++] = b;
            }

            internal String GetString()
            {
                if (_byteCount > 0) FlushBytes();
                return (_charCount > 0) ? (new String(_charBuffer, 0, _charCount)) : String.Empty;
            }

            private void FlushBytes()
            {
                if (_byteCount > 0) {
                    _charCount += _encoding.GetChars(_byteBuffer, 0, _byteCount, _charBuffer, _charCount);
                    _byteCount = 0;
                }
            }
        }

        #endregion
    }
}
