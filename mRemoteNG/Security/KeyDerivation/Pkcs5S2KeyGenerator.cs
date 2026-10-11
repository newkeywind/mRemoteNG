using System;
using System.Linq;
using System.Security.Cryptography;

namespace mRemoteNG.Security.KeyDerivation
{
    public class Pkcs5S2KeyGenerator : IKeyDerivationFunction
    {
        private readonly int _iterations;
        private readonly int _keyBitSize;

        public Pkcs5S2KeyGenerator(int keyBitSize = 256, int iterations = 1000)
        {
            if (iterations < 1000)
                throw new ArgumentOutOfRangeException($"Minimum value of {nameof(iterations)} is 1000");
            if (keyBitSize < 0)
                throw new ArgumentOutOfRangeException($"{nameof(keyBitSize)} must be positive");
            _keyBitSize = keyBitSize;
            _iterations = iterations;
        }

        public byte[] DeriveKey(string password, byte[] salt)
        {
            byte[] passwordInBytes = password.ToCharArray().Select(c => (byte)c).ToArray();

            byte[] keyBytes = Rfc2898DeriveBytes.Pbkdf2(passwordInBytes, salt, _iterations, HashAlgorithmName.SHA1, _keyBitSize / 8);
            return keyBytes;
        }
    }
}