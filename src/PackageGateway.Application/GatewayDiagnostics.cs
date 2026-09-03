using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PackageGateway.Application;

public static class GatewayDiagnostics
{
    public const string ActivitySourceName = "PackageGateway";
    public const string MeterName = "PackageGateway";
    public static readonly ActivitySource Activities = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> AcquisitionRequests =
        Meter.CreateCounter<long>("package_gateway.acquisition.requests");

    public static readonly Counter<long> AcquisitionOutcomes =
        Meter.CreateCounter<long>("package_gateway.acquisition.outcomes");

    public static readonly Counter<long> MetadataCacheOutcomes =
        Meter.CreateCounter<long>("package_gateway.metadata_cache.outcomes");

    public static readonly Counter<long> PolicyOutcomes = Meter.CreateCounter<long>("package_gateway.policy.outcomes");

    public static readonly Counter<long> BackgroundJobOutcomes =
        Meter.CreateCounter<long>("package_gateway.background_job.outcomes");

    public static readonly Histogram<double> ScanDuration =
        Meter.CreateHistogram<double>("package_gateway.scan.duration", "s");

    public static readonly Histogram<double> DatabaseSaveDuration =
        Meter.CreateHistogram<double>("package_gateway.database.save.duration", "s");
}