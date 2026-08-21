# Database Schema Specification (PostgreSQL)

## Table: `BatchSessions`
* `Id` (UUID, Primary Key, Default: `gen_random_uuid()`)
* `CreatedAt` (Timestamp with time zone, Not Null, Default: `NOW()`)
* `TotalFiles` (Integer, Not Null)
* `SuccessfulFiles` (Integer, Not Null)
* `FailedFiles` (Integer, Not Null)
* `Status` (Varchar(50), Not Null) -- Options: Pending, Processing, Completed, Failed

## Table: `RemediationLogs`
* `Id` (UUID, Primary Key, Default: `gen_random_uuid()`)
* `BatchSessionId` (UUID, Not Null, Foreign Key references `BatchSessions(Id)` on Delete Cascade)
* `OriginalFileName` (Varchar(255), Not Null)
* `FileSizeBytes` (BigInt, Not Null)
* `IsOcrApplied` (Boolean, Not Null, Default: False)
* `IsStructureRebuilt` (Boolean, Not Null, Default: False)
* `IsAccessibleTagged` (Boolean, Not Null, Default: False)
* `ProcessingDurationMs` (Integer, Not Null)
* `ErrorMessage` (Text, Nullable)