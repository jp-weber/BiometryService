﻿#if WINDOWS_UWP
using System;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Uno.Extensions;
using Uno.Logging;
using Windows.Foundation.Collections;
using Windows.Storage;
using AsyncLock = Uno.Threading.AsyncLock;

namespace BiometryService
{
	public class BiometryService : IBiometryService
	{
		private AsyncLock _asyncLock;

		private IObservable<bool> _isEnabled;
		private IObservable<bool> _isSupported;

		private IPropertySet _keys;

		public BiometryService(bool supported, bool enrolled, IScheduler backgroundScheduler)
		{
			backgroundScheduler.Validation().NotNull(nameof(backgroundScheduler));

			_keys = ApplicationData.Current.LocalSettings.Values;

			_asyncLock = new AsyncLock();

			_isSupported =
				Observable.Never<bool>()
					.StartWith(supported)
					.Replay(1, backgroundScheduler)
					.RefCount();

			_isEnabled =
				_isSupported
					.Select(isSupported => isSupported && enrolled)
					.Replay(1, backgroundScheduler)
					.RefCount();
		}

		public async Task<string> Decrypt(CancellationToken ct, string key, byte[] data)
		{
			if (this.Log().IsEnabled(LogLevel.Debug))
			{
				this.Log().Debug($"Decrypting the fingerprint (key name: '{key}').");
			}

			key.Validation().NotNullOrEmpty(nameof(key));
			data.Validation().NotNull(nameof(data));
			data.Validation().IsTrue(array => array.Length >= 32, nameof(data), "Data is invalid.");

			await AssertIsEnabled(ct);

			using (await _asyncLock.LockAsync(ct))
			{
				using (Aes aes = Aes.Create())
				{
					aes.BlockSize = 128;
					aes.KeySize = 256;
					aes.Mode = CipherMode.CBC;
					aes.Padding = PaddingMode.PKCS7;

					var iv = new byte[16];
					Array.ConstrainedCopy(data, 0, iv, 0, 16);

					aes.IV = iv;
					aes.Key = RetrieveKey(key);

					using (var ms = new MemoryStream(data))
					using (var outputStream = new MemoryStream())
					using (var cryptoStream = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read))
					{
						ms.Seek(16, SeekOrigin.Begin);

						await cryptoStream.CopyToAsync(outputStream, 81920, ct);

						if (this.Log().IsEnabled(LogLevel.Information))
						{
							this.Log().Info($"Successfully decrypted the fingerprint (key name: '{key}').");
						}

						return Encoding.ASCII.GetString(outputStream.ToArray());
					}
				}
			}
		}

		public async Task<byte[]> Encrypt(CancellationToken ct, string key, string value)
		{
			if (this.Log().IsEnabled(LogLevel.Debug))
			{
				this.Log().Debug($"Encrypting the fingerprint (key name: '{key}').");
			}

			key.Validation().NotNullOrEmpty(nameof(key));
			value.Validation().NotNull(nameof(value));

			await AssertIsEnabled(ct);

			using (await _asyncLock.LockAsync(ct))
			{
				using (Aes aes = Aes.Create())
				{
					aes.BlockSize = 128;
					aes.KeySize = 256;
					aes.Mode = CipherMode.CBC;
					aes.Padding = PaddingMode.PKCS7;

					aes.GenerateIV();
					aes.GenerateKey();

					SaveKey(key, aes.Key);

					using (var ms = new MemoryStream())
					using (var cryptoStream = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
					{
						var valueBytes = Encoding.ASCII.GetBytes(value);

						await cryptoStream.WriteAsync(valueBytes, 0, valueBytes.Length, ct);

						cryptoStream.FlushFinalBlock();

						if (this.Log().IsEnabled(LogLevel.Information))
						{
							this.Log().Info($"Successfully encrypted the fingerprint (key name: '{key}').");
						}

						return aes.IV
							.Concat(ms.ToArray())
							.ToArray();
					}
				}
			}
		}

		public BiometryCapabilities GetCapabilities()
		{
			return new BiometryCapabilities(BiometryType.Fingerprint, true, true);
		}

		public async Task<BiometryResult> ValidateIdentity(CancellationToken ct)
		{
			if (this.Log().IsEnabled(LogLevel.Debug))
			{
				this.Log().Debug("Authenticating the fingerprint.");
			}

			await AssertIsEnabled(ct);

			if (this.Log().IsEnabled(LogLevel.Information))
			{
				this.Log().Info("Successfully authenticated the fingerprint.");
			}

			return new BiometryResult();
		}

		private async Task AssertIsEnabled(CancellationToken ct)
		{
			var enabled = await _isEnabled.FirstAsync();

			if (!enabled)
			{
				var supported = await _isSupported.FirstAsync();

				if (supported)
				{
					throw new InvalidOperationException("No fingerprint(s) registered.");
				}
				else
				{
					if (this.Log().IsEnabled(LogLevel.Warning))
					{
						this.Log().Warn("Fingerprint authentication is not available.");
					}

					throw new NotSupportedException("Fingerprint authentication is not available.");
				}
			}
		}

		public IObservable<bool> GetAndObserveIsEnabled() => _isEnabled;

		public IObservable<bool> GetAndObserveIsSupported() => _isSupported;

		private byte[] RetrieveKey(string name)
		{
			if (this.Log().IsEnabled(LogLevel.Debug))
			{
				this.Log().Debug($"Retrieving the key (name: '{name}').");
			}

			if (_keys.TryGetValue(name, out var value))
			{
				if (this.Log().IsEnabled(LogLevel.Information))
				{
					this.Log().Info($"Successfully retrieved the key (name: '{name}').");
				}

				return Convert.FromBase64String(value as string);
			}
			else
			{
				if (this.Log().IsEnabled(LogLevel.Error))
				{
					this.Log().Error("The key was not found.");
				}

				throw new ArgumentException("Key not found.");
			}
		}

		private void SaveKey(string name, byte[] key)
		{
			_keys[name] = Convert.ToBase64String(key);
		}
	}
}
#endif