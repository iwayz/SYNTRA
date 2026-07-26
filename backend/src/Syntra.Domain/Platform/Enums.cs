namespace Syntra.Domain.Platform;

public enum TenantStatus
{
    Trial,
    Active,
    Suspended,
    Cancelled
}

public enum MaintenanceFeeStatus
{
    Paid,
    Overdue
}

public enum ModuleCategory
{
    Core,
    Premium
}

public enum MigrationRunStatus
{
    Success,
    Failed,
    InProgress
}

public enum OnboardingStepStatus
{
    Success,
    Failed
}
