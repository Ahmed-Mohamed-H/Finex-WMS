using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Finex.Infrastructure.Security
{
    public static class HmacSignatureUtility
    {
        private const int Sha256HashLengthBytes = 32;

        /// <summary>
        /// يحسب بصمة HMAC-SHA256 للنص الموحد:
        /// Method\nPath\nTimestamp\nIdempotencyKey\nBody
        /// </summary>
        public static byte[] ComputeCanonicalHmacBytes(
            string method,
            string path,
            string timestamp,
            string idempotencyKey,
            ReadOnlySpan<byte> bodyBytes,
            string secret)
        {
            string prefix = $"{method}\n{path}\n{timestamp}\n{idempotencyKey}\n";
            int prefixByteCount = Encoding.UTF8.GetByteCount(prefix);
            int totalLength = prefixByteCount + bodyBytes.Length;

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(totalLength);
            try
            {
                int written = Encoding.UTF8.GetBytes(prefix, rentedBuffer.AsSpan(0, prefixByteCount));
                bodyBytes.CopyTo(rentedBuffer.AsSpan(written, bodyBytes.Length));

                byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
                return HMACSHA256.HashData(keyBytes, rentedBuffer.AsSpan(0, totalLength));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rentedBuffer.AsSpan(0, totalLength));
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        /// <summary>
        /// يحسب بصمة HMAC-SHA256 لرد السيرفر مربوطة بالطلب الأصلي وحالة الاستجابة:
        /// Timestamp\nIdempotencyKey\nStatusCode\nResponseBody
        /// </summary>
        public static byte[] ComputeResponseHmacBytes(
            string timestamp,
            string idempotencyKey,
            int statusCode,
            ReadOnlySpan<byte> responseBodyBytes,
            string secret)
        {
            string prefix = $"{timestamp}\n{idempotencyKey}\n{statusCode}\n";
            int prefixByteCount = Encoding.UTF8.GetByteCount(prefix);
            int totalLength = prefixByteCount + responseBodyBytes.Length;

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(totalLength);
            try
            {
                int written = Encoding.UTF8.GetBytes(prefix, rentedBuffer.AsSpan(0, prefixByteCount));
                responseBodyBytes.CopyTo(rentedBuffer.AsSpan(written, responseBodyBytes.Length));

                byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
                return HMACSHA256.HashData(keyBytes, rentedBuffer.AsSpan(0, totalLength));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(rentedBuffer.AsSpan(0, totalLength));
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }

        /// <summary>
        /// يتحقق من التوقيع باستخدام CurrentSecret ثم PreviousSecret بمقارنة ثابتة الزمن (FixedTimeEquals).
        /// </summary>
        public static bool TryVerifyDualKey(
            string providedHexSignature,
            Func<string, byte[]> expectedHashFactory,
            string currentSecret,
            string? previousSecret,
            out string? matchedSecret)
        {
            matchedSecret = null;

            if (string.IsNullOrWhiteSpace(providedHexSignature) ||
                providedHexSignature.Length != Sha256HashLengthBytes * 2)
            {
                return false;
            }

            Span<byte> providedBytes = stackalloc byte[Sha256HashLengthBytes];
            if (!TryDecodeHex(providedHexSignature.AsSpan(), providedBytes))
            {
                return false;
            }

            byte[] currentExpected = expectedHashFactory(currentSecret);
            bool matchesCurrent = CryptographicOperations.FixedTimeEquals(providedBytes, currentExpected);

            bool matchesPrevious = false;
            if (!string.IsNullOrWhiteSpace(previousSecret))
            {
                byte[] previousExpected = expectedHashFactory(previousSecret);
                matchesPrevious = CryptographicOperations.FixedTimeEquals(providedBytes, previousExpected);
            }

            if (matchesCurrent)
            {
                matchedSecret = currentSecret;
                return true;
            }

            if (matchesPrevious)
            {
                matchedSecret = previousSecret;
                return true;
            }

            return false;
        }

        private static bool TryDecodeHex(ReadOnlySpan<char> hex, Span<byte> destination)
        {
            if (hex.Length != destination.Length * 2)
            {
                return false;
            }

            for (int i = 0; i < destination.Length; i++)
            {
                int high = ParseHexNibble(hex[i * 2]);
                int low = ParseHexNibble(hex[i * 2 + 1]);
                if (high < 0 || low < 0)
                {
                    return false;
                }

                destination[i] = (byte)((high << 4) | low);
            }

            return true;
        }

        private static int ParseHexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}