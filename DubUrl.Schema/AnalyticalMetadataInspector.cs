using System.Data;
using System.Data.Common;
using DubUrl.Authentication;
using DubUrl.Discovery;

namespace DubUrl.Schema;

/// <summary>Inspects the analytical objects visible to the current connection.</summary>
public sealed class AnalyticalMetadataInspector
{
    /// <summary>Opens and disposes a connection to the catalog selected by the URL.</summary>
    public async ValueTask<AnalyticalMetadata> InspectAsync(ConnectionUrl source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        IAnalyticalMetadataReader? reader = null;
        try
        {
            reader = FindReader(source);
            if (reader is null)
                return Failure(AnalyticalCoverage.Unsupported, "The provider does not support analytical metadata discovery.");

            using var connection = await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception, reader))
        {
            return Failure(AnalyticalCoverage.Inaccessible, "The analytical metadata could not be accessed.");
        }
    }

    /// <summary>Uses the URL's provider registration and leaves the supplied open connection with the caller.</summary>
    public async ValueTask<AnalyticalMetadata> InspectAsync(ConnectionUrl source, IDbConnection borrowed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(borrowed);
        cancellationToken.ThrowIfCancellationRequested();
        if (borrowed.State != ConnectionState.Open)
            throw new ArgumentException("A borrowed connection must already be open.", nameof(borrowed));

        IAnalyticalMetadataReader? reader = null;
        try
        {
            reader = FindReader(source);
            return reader is null
                ? Failure(AnalyticalCoverage.Unsupported, "The provider does not support analytical metadata discovery.")
                : await reader.ReadAsync(borrowed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception, reader))
        {
            return Failure(AnalyticalCoverage.Inaccessible, "The analytical metadata could not be accessed.");
        }
    }

    private static IAnalyticalMetadataReader? FindReader(ConnectionUrl source)
        => source.TryGetCapability<AnalyticalDiscoveryCapability>(out var capability) ? capability?.Reader : null;

    private static bool IsConnectionFailure(Exception exception, IAnalyticalMetadataReader? reader)
        => exception is DbException or DubUrlException or ConnectionAuthenticationException or InvalidOperationException or TimeoutException
            || (exception is not OperationCanceledException && reader?.IsAccessFailure(exception) == true);

    private static AnalyticalMetadata Failure(AnalyticalCoverage coverage, string message)
        => new([], [], Enum.GetValues<AnalyticalFeature>().ToDictionary(feature => feature, _ => coverage),
            [new AnalyticalDiagnostic(null, coverage, message)]);
}
