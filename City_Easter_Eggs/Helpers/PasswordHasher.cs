﻿#region Using

using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Identity;

#endregion

namespace City_Easter_Eggs.Helpers;

// Copy from ASPNetCore.Identity
public class PasswordHasher
{
    /* 
     * PBKDF2 with HMAC-SHA512, 128-bit salt, 256-bit subkey, 100_000 iterations.
     * Format: { 0x01, prf (UInt32), iter count (UInt32), salt length (UInt32), salt, subkey }
     * (All UInt32s are stored big-endian.)
     */

    private readonly int _iterCount = 100_000;
    private readonly RandomNumberGenerator _rng;

    public PasswordHasher()
    {
        _rng = RandomNumberGenerator.Create();
    }

    public virtual string HashPassword(string password)
    {
        return Convert.ToBase64String(HashPasswordV3(password, _rng));
    }

    private byte[] HashPasswordV3(string password, RandomNumberGenerator rng)
    {
        return HashPasswordV3(password, rng,
            KeyDerivationPrf.HMACSHA512,
            _iterCount,
            128 / 8,
            256 / 8);
    }

    private static byte[] HashPasswordV3(string password, RandomNumberGenerator rng, KeyDerivationPrf prf, int iterCount, int saltSize, int numBytesRequested)
    {
        // Produce a version 3 (see comment above) text hash.
        var salt = new byte[saltSize];
        rng.GetBytes(salt);
        byte[] subkey = KeyDerivation.Pbkdf2(password, salt, prf, iterCount, numBytesRequested);

        var outputBytes = new byte[13 + salt.Length + subkey.Length];
        outputBytes[0] = 0x01; // format marker
        WriteNetworkByteOrder(outputBytes, 1, (uint) prf);
        WriteNetworkByteOrder(outputBytes, 5, (uint) iterCount);
        WriteNetworkByteOrder(outputBytes, 9, (uint) saltSize);
        Buffer.BlockCopy(salt, 0, outputBytes, 13, salt.Length);
        Buffer.BlockCopy(subkey, 0, outputBytes, 13 + saltSize, subkey.Length);
        return outputBytes;
    }

    /// <summary>
    /// Returns a <see cref="PasswordVerificationResult" /> indicating the result of a password hash comparison.
    /// </summary>
    /// <param name="hashedPassword">The hash value for a user's stored password.</param>
    /// <param name="providedPassword">The password supplied for comparison.</param>
    /// <returns>A <see cref="PasswordVerificationResult" /> indicating the result of a password hash comparison.</returns>
    /// <remarks>Implementations of this method should be time consistent.</remarks>
    public virtual PasswordVerificationResult VerifyHashedPassword(string hashedPassword, string providedPassword)
    {
        byte[] decodedHashedPassword = Convert.FromBase64String(hashedPassword);
        if (decodedHashedPassword.Length == 0)
            return PasswordVerificationResult.Failed;

        if (VerifyHashedPasswordV3(decodedHashedPassword, providedPassword, out int embeddedIterCount, out KeyDerivationPrf prf))
        {
            // If this hasher was configured with a higher iteration count, change the entry now.
            if (embeddedIterCount < _iterCount) return PasswordVerificationResult.SuccessRehashNeeded;

            // V3 now requires SHA512. If the old PRF is SHA1 or SHA256, upgrade to SHA512 and rehash.
            if (prf == KeyDerivationPrf.HMACSHA1 || prf == KeyDerivationPrf.HMACSHA256) return PasswordVerificationResult.SuccessRehashNeeded;

            return PasswordVerificationResult.Success;
        }

        return PasswordVerificationResult.Failed;
    }

    private static bool VerifyHashedPasswordV3(byte[] hashedPassword, string password, out int iterCount, out KeyDerivationPrf prf)
    {
        iterCount = default(int);
        prf = default(KeyDerivationPrf);

        try
        {
            // Read header information
            prf = (KeyDerivationPrf) ReadNetworkByteOrder(hashedPassword, 1);
            iterCount = (int) ReadNetworkByteOrder(hashedPassword, 5);
            var saltLength = (int) ReadNetworkByteOrder(hashedPassword, 9);

            // Read the salt: must be >= 128 bits
            if (saltLength < 128 / 8) return false;
            var salt = new byte[saltLength];
            Buffer.BlockCopy(hashedPassword, 13, salt, 0, salt.Length);

            // Read the subkey (the rest of the payload): must be >= 128 bits
            int subkeyLength = hashedPassword.Length - 13 - salt.Length;
            if (subkeyLength < 128 / 8) return false;
            var expectedSubkey = new byte[subkeyLength];
            Buffer.BlockCopy(hashedPassword, 13 + salt.Length, expectedSubkey, 0, expectedSubkey.Length);

            // Hash the incoming password and verify it
            byte[] actualSubkey = KeyDerivation.Pbkdf2(password, salt, prf, iterCount, subkeyLength);
            return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
        }
        catch
        {
            // This should never occur except in the case of a malformed payload, where
            // we might go off the end of the array. Regardless, a malformed payload
            // implies verification failed.
            return false;
        }
    }

    private static uint ReadNetworkByteOrder(byte[] buffer, int offset)
    {
        return ((uint) buffer[offset + 0] << 24)
               | ((uint) buffer[offset + 1] << 16)
               | ((uint) buffer[offset + 2] << 8)
               | buffer[offset + 3];
    }

    private static void WriteNetworkByteOrder(byte[] buffer, int offset, uint value)
    {
        buffer[offset + 0] = (byte) (value >> 24);
        buffer[offset + 1] = (byte) (value >> 16);
        buffer[offset + 2] = (byte) (value >> 8);
        buffer[offset + 3] = (byte) (value >> 0);
    }
}