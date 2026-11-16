using System;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static void Main()
    {
        // 1. Replace with the exact CurrentSecret configured in your ERP server
        string secret = "your_super_secret_key_minimum_32_characters_long_here";

        // 2. Request parameters to test in Scalar
        string method = "POST";
        string path = "/api/simulator/handover"; // Update with your actual endpoint route
        string body = "{\"warehouseId\":1,\"quantity\":500}"; // Must match the exact JSON body sent in Scalar

        // 3. Generate current Unix timestamp and a unique Idempotency Key (GUID)
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string idempotencyKey = Guid.NewGuid().ToString("D");

        // 4. Construct the strict canonical format (Method\nPath\nTimestamp\nIdempotencyKey\nBody)
        string canonicalPrefix = $"{method}\n{path}\n{timestamp}\n{idempotencyKey}\n";

        // Combine raw bytes accurately to match server-side verification
        byte[] prefixBytes = Encoding.UTF8.GetBytes(canonicalPrefix);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

        byte[] totalPayload = new byte[prefixBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, totalPayload, 0, prefixBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, totalPayload, prefixBytes.Length, bodyBytes.Length);

        // 5. Compute HMAC-SHA256 signature
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] hashBytes = HMACSHA256.HashData(keyBytes, totalPayload);
        string signature = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // 6. Output headers ready to be pasted into Scalar UI
        Console.WriteLine("=================================================================");
        Console.WriteLine(" Copy and paste these values into Scalar Authentication headers:");
        Console.WriteLine("=================================================================");
        Console.WriteLine($"X-Timestamp       : {timestamp}");
        Console.WriteLine($"X-Idempotency-Key : {idempotencyKey}");
        Console.WriteLine($"X-Signature       : {signature}");
        Console.WriteLine("=================================================================");

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}