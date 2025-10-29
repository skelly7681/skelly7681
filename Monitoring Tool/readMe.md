# Monitoring Tool (.NET Framework)

This document provides setup, configuration, and technical parameter details for the Monitoring Tool built in C# (.NET Framework). The utility monitors directories, files, and Windows services, and reports via email.

---

## Features

* Detects missing daily files at a specific time.
* Checks if folders are not empty.
* Monitors file count thresholds.
* Scans log files for `ERROR` entries, allowing exception filters.
* Checks Windows services for status and startup type compliance.
* Sends HTML summary reports by email.

---

## Configuration Overview

The tool relies on two files for configuration:

1. **App.config** — Defines system-level paths, email credentials, and log4net settings.
2. **Config.csv** — Defines monitoring rules and method execution parameters.

---

## App.config Example

```xml
<configuration>
  <appSettings>
    <!-- Configuration CSV path -->
    <add key="Config_Path" value="C:\\Monitoring\\Config.csv" />

    <!-- SMTP Email Settings -->
    <add key="EmailFrom" value="monitoring@example.com" />
    <add key="EmailTo" value="recipient@example.com" />
    <add key="SMTP_SERVER" value="smtp.office365.com" />
    <add key="SMTP_PORT" value="587" />
    <add key="SMTP_PASS" value="your_password_here" />
  </appSettings>

  <log4net>
    <appender name="FileAppender" type="log4net.Appender.RollingFileAppender">
      <file value="C:\\Monitoring\\Logs\\MonitoringTool.log" />
      <appendToFile value="true" />
      <rollingStyle value="Size" />
      <maxSizeRollBackups value="5" />
      <maximumFileSize value="5MB" />
      <layout type="log4net.Layout.PatternLayout">
        <conversionPattern value="%date [%thread] %-5level %logger - %message%newline" />
      </layout>
    </appender>
    <root>
      <level value="INFO" />
      <appender-ref ref="FileAppender" />
    </root>
  </log4net>
</configuration>
```

---

## Config.csv Format (NEW ORDER)

Each line of **Config.csv** defines one monitoring task with **four columns in this exact order**:

| Method | Path or Service Name | Active (Y/N) | Parameter(s) |
| ------ | -------------------- | ------------ | ------------ |

**Important:** In this configuration, the **Active flag is the 3rd column** and the method-specific **Parameter(s) are the 4th column.** This matches the current code where `columns[2]` is the active flag and `columns[3]` is the parameter/exceptions.

Below are examples for each supported method using this order.

---

### 1) `check_missing_daily_file`

**Example line:**

```
check_missing_daily_file,C:\Reports,Y,08:00
```

**Columns:**

* **Method:** `check_missing_daily_file`
* **Path:** `C:\Reports`
* **Active:** `Y`
* **Param (Time):** `08:00` (24-hour format)

**Behavior:** Checks the folder for a file created today after the specified time. Alerts if none is found.

---

### 2) `is_folder_not_empty`

**Example line:**

```
is_folder_not_empty,C:\Inbound,Y,
```

**Columns:**

* **Method:** `is_folder_not_empty`
* **Path:** `C:\Inbound`
* **Active:** `Y`
* **Param:** (blank/not used)

**Behavior:** Lists files if the folder is not empty.

---

### 3) `check_file_counts_above_threshold`

**Example line:**

```
check_file_counts_above_threshold,C:\Archive,Y,100
```

**Columns:**

* **Method:** `check_file_counts_above_threshold`
* **Path:** `C:\Archive`
* **Active:** `Y`
* **Param (Threshold):** `100`

**Behavior:** Adds a result if file count > threshold.

---

### 4) `check_error`

**Example line (with exclusions):**

```
check_error,C:\Logs,Y,"TimeoutException";"SSL Handshake";"KnownBenign"
```

**Example line (no exclusions):**

```
check_error,C:\Logs,Y,
```

**Columns:**

* **Method:** `check_error`
* **Path:** `C:\Logs`
* **Active:** `Y`
* **Param (Exceptions):** Semicolon-delimited list of phrases. Example shows three exclusions.

**Behavior:** Scans log files (last-write-time > last run) for lines containing `ERROR`. If the 4th column contains exclusion phrases, lines containing any of those phrases are ignored. The implementation groups multi-line error blocks in `CheckError` and (depending on your current build) either line-level or block-level capture in `CheckErrorEx`.

**Format of the exceptions field:**

* Use semicolons between phrases.
* Each phrase may be quoted: `"Phrase A";"Phrase B"` (your current parser strips quotes).
* Empty field = report all `ERROR` lines.

---

### 5) `services`

**Example line:**

```
services,Spooler,Y,Running|Automatic
```

**Columns:**

* **Method:** `services`
* **Service Name:** `Spooler`
* **Active:** `Y`
* **Param:** `ExpectedStatus|ExpectedStartupType`

  * Example: `Running|Automatic`
  * Leave either side empty to skip that expectation (e.g., `|Automatic` or `Running|`).

**Behavior:** Compares the service's **actual** status and startup type to the **expected** values. Adds a result row when mismatched.

---

## Execution Flow

1. The application reads `Config.csv` entries.
2. Executes each active check in order.
3. Aggregates results into HTML tables.
4. Sends a summary email if any issues are detected.
5. Logs detailed information to the log4net file.

---

## Running the Tool

### Command Line

Run the executable manually:

```bash
Monitoring_Tool.exe
```

### Windows Task Scheduler

1. Open Task Scheduler.
2. Create a new task → Trigger: Daily.
3. Action: Start a program → `Monitoring_Tool.exe`.
4. Option: “Run whether user is logged on or not.”

---

## Troubleshooting

| Issue                       | Possible Cause                                | Fix                                                                     |
| --------------------------- | --------------------------------------------- | ----------------------------------------------------------------------- |
| No email sent               | No issues or invalid SMTP config              | Verify App.config settings                                              |
| Application appears to hang | Very large log file or empty exception string | Trim logs, rotate files, or ensure exceptions list has no empty entries |
| UnauthorizedAccessException | Insufficient permissions                      | Run as Administrator                                                    |
| Invalid config entry        | Wrong CSV column count                        | Ensure exactly 4 columns in the order shown above                       |

---

## Requirements

* Windows with .NET Framework 4.7 or newer
* SMTP access (Office365, Gmail, or relay)
* Read/write access to target folders

---

## Author

Monitoring Tool by Sean Kelly
© 2025 — All Rights Reserved
