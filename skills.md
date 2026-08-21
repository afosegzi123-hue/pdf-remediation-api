# Engineering Skills & Guidelines

* **Null Safety:** Strict nullable reference types must be enforced across all C# code files (`<Nullable>enable</Nullable>`).
* **Resource Disposals:** All stream buffers (`ZipArchive`, `MemoryStream`, `FileStream`) must be wrapped in `using` blocks or declarations to prevent native memory leaks inside container instances.
* **Fault Isolation:** Individual file corruptions inside a submitted ZIP bundle must be caught locally per file iteration, logged as failed items with messages, allowing the wider batch processing loop to complete successfully.
* **High-Throughput Streaming:** Never buffer full batch archives entirely in application memory; use streaming stream pipelines where possible.