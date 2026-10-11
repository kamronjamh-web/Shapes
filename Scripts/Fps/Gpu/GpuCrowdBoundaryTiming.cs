namespace SuperGatchaRebuild.Fps.Gpu;

// CPU wall intervals. Dispatch/wait includes submission and synchronization;
// WorldPrepare occurs before the contact boundary in CPU preparation timings.
internal readonly record struct GpuCrowdBoundaryTiming(double BodyPackMs, double DrivePackMs,
    double UploadMs, double DispatchAndWaitMs, double ReadbackMs, double ValidateMs, double ApplyMs,
    double WorldPrepareMs, bool Succeeded, double GpuExecutionMs)
{
    internal double PackingMs => BodyPackMs + DrivePackMs;
    internal double BoundaryTotalMs => PackingMs + UploadMs + DispatchAndWaitMs + ReadbackMs + ValidateMs + ApplyMs;
}
