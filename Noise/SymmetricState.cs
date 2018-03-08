using System;
using System.Security.Cryptography;

namespace Noise
{
	/// <summary>
	/// A SymmetricState object contains a CipherState plus ck (a chaining
	/// key of HashLen bytes) and h (a hash output of HashLen bytes).
	/// </summary>
	internal sealed class SymmetricState<CipherType, DhType, HashType> : IDisposable
		where CipherType : Cipher, new()
		where DhType : Dh, new()
		where HashType : Hash, new()
	{
		private readonly Cipher cipher = new CipherType();
		private readonly DhType dh = new DhType();
		private readonly Hash hash = new HashType();
		private readonly CipherState<CipherType> state = new CipherState<CipherType>();
		private byte[] ck;
		private readonly byte[] h;
		private bool disposed;

		/// <summary>
		/// Initializes a new SymmetricState with an
		/// arbitrary-length protocolName byte sequence.
		/// </summary>
		public SymmetricState(byte[] protocolName)
		{
			if (protocolName == null)
			{
				throw new ArgumentNullException(nameof(protocolName));
			}

			int length = hash.HashLen;

			ck = new byte[length];
			h = new byte[length];

			if (protocolName.Length <= length)
			{
				Array.Copy(protocolName, h, protocolName.Length);
			}
			else
			{
				hash.AppendData(protocolName);
				hash.GetHashAndReset(h);
			}

			Array.Copy(h, ck, length);
		}

		/// <summary>
		/// Sets ck, tempK = HKDF(ck, inputKeyMaterial, 2).
		/// If HashLen is 64, then truncates tempK to 32 bytes.
		/// Calls InitializeKey(tempK).
		/// </summary>
		public void MixKey(byte[] inputKeyMaterial)
		{
			ValidateInputKeyMaterial(inputKeyMaterial);

			var (ck, tempK) = Hkdf<HashType>.ExtractAndExpand2(this.ck, inputKeyMaterial);

			Array.Clear(this.ck, 0, this.ck.Length);
			this.ck = ck;

			state.InitializeKey(Truncate(tempK));
		}

		/// <summary>
		/// Sets h = HASH(h || data).
		/// </summary>
		public void MixHash(ReadOnlySpan<byte> data)
		{
			hash.AppendData(h);
			hash.AppendData(data);
			hash.GetHashAndReset(h);
		}

		/// <summary>
		/// Sets ck, tempH, tempK = HKDF(ck, inputKeyMaterial, 3).
		/// Calls MixHash(tempH).
		/// If HashLen is 64, then truncates tempK to 32 bytes.
		/// Calls InitializeKey(tempK).
		/// </summary>
		public void MixKeyAndHash(byte[] inputKeyMaterial)
		{
			ValidateInputKeyMaterial(inputKeyMaterial);

			var (ck, tempH, tempK) = Hkdf<HashType>.ExtractAndExpand3(this.ck, inputKeyMaterial);

			Array.Clear(this.ck, 0, this.ck.Length);
			this.ck = ck;

			MixHash(tempH);
			state.InitializeKey(Truncate(tempK));
		}

		/// <summary>
		/// Returns h. This function should only be called at the end of
		/// a handshake, i.e. after the Split() function has been called.
		/// </summary>
		public byte[] GetHandshakeHash()
		{
			byte[] handshakeHash = new byte[h.Length];
			Array.Copy(h, handshakeHash, h.Length);

			return handshakeHash;
		}

		/// <summary>
		/// Sets ciphertext = EncryptWithAd(h, plaintext),
		/// calls MixHash(ciphertext), and returns ciphertext.
		/// </summary>
		public int EncryptAndHash(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
		{
			int bytesWritten = state.EncryptWithAd(h, plaintext, ciphertext);
			MixHash(ciphertext.Slice(0, bytesWritten));

			return bytesWritten;
		}

		/// <summary>
		/// Sets plaintext = DecryptWithAd(h, ciphertext),
		/// calls MixHash(ciphertext), and returns plaintext.
		/// </summary>
		public int DecryptAndHash(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext)
		{
			var bytesRead = state.DecryptWithAd(h, ciphertext, plaintext);
			MixHash(ciphertext);

			return bytesRead;
		}

		/// <summary>
		/// Returns a pair of CipherState objects for encrypting transport messages.
		/// </summary>
		public (CipherState<CipherType> c1, CipherState<CipherType> c2) Split()
		{
			var (tempK1, tempK2) = Hkdf<HashType>.ExtractAndExpand2(ck, null);

			var c1 = new CipherState<CipherType>();
			var c2 = new CipherState<CipherType>();

			c1.InitializeKey(Truncate(tempK1));
			c2.InitializeKey(Truncate(tempK2));

			return (c1, c2);
		}

		/// <summary>
		/// Returns true if k is non-empty, false otherwise.
		/// </summary>
		public bool HasKey()
		{
			return state.HasKey();
		}

		private void ValidateInputKeyMaterial(byte[] inputKeyMaterial)
		{
			if (inputKeyMaterial == null)
			{
				throw new ArgumentNullException(nameof(inputKeyMaterial));
			}

			int length = inputKeyMaterial.Length;

			if (length != 0 && length != Constants.KeySize && length != dh.DhLen)
			{
				throw new CryptographicException("Input key material must be either 0 bytes, 32 byte, or DhLen bytes long.");
			}
		}

		private static byte[] Truncate(byte[] key)
		{
			if (key.Length == Constants.KeySize)
			{
				return key;
			}

			var temp = new byte[Constants.KeySize];

			Array.Copy(key, temp, temp.Length);
			Array.Clear(key, 0, key.Length);

			return temp;
		}

		public void Dispose()
		{
			if (!disposed)
			{
				hash.Dispose();
				state.Dispose();
				Array.Clear(ck, 0, ck.Length);
				Array.Clear(h, 0, h.Length);
				disposed = true;
			}
		}
	}
}
