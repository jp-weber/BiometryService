using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BiometryService;

/// <summary>
/// Shared class used accross all implementations.
/// </summary>
public sealed partial class BiometryService
{
#if __ANDROID__ || __WINDOWS__ || __MACCATALYST__ || __IOS__
	/// <summary>
	/// Validates biometry capabilities and throw the right exception if they aren't valide.
	/// </summary>
	/// <param name="ct"><see cref="CancellationToken"/>.</param>
	/// <returns><see cref="Task"/>.</returns>
	/// <exception cref="BiometryException">.</exception>
	private async Task ValidateBiometryCapabilities(CancellationToken ct)
	{
		if (Logger.IsEnabled(LogLevel.Debug))
		{
			Logger.LogDebug("Validating biometry capabilities.");
		}

		var biometryCapabilities = await GetCapabilities(ct);
		if (!biometryCapabilities.IsEnabled)
		{
			var reason = biometryCapabilities.IsSupported ? BiometryExceptionReason.NotEnrolled : BiometryExceptionReason.Unavailable;
			var message = biometryCapabilities.IsSupported ? "Biometrics are not enrolled on this device" : "Biometry is not available on this device";

			throw new BiometryException(reason, message);
		}

		if (Logger.IsEnabled(LogLevel.Information))
		{
			Logger.LogDebug("Biometry capabilities have been successfully validated.");
		}
	}
#endif
}
