# PulseMirror

**PulseMirror** is a lightweight, zero-dependency Windows desktop client for real-time screen mirroring, remote control, and screen capture of Rigol digital oscilloscopes (specifically targeting the popular **DS1000E series**, such as the **DS1052E** and **DS1102E**).

Unlike typical bench tools, PulseMirror talks directly to the instrument via the standard **USBTMC** (USB Test and Measurement Class) protocol over **WinUSB**—completely eliminating the need for heavyweight vendor runtimes like NI-VISA, Keysight IO Libraries Suite, or UltraSigma.

---

## Key Features

- **Direct USBTMC over WinUSB**
  Pure C# implementation of USBTMC framing (Bulk-OUT/Bulk-IN packets) sitting directly on top of Microsoft's native `winusb.dll`. Plug and play with standard WinUSB drivers (installable via Zadig) without proprietary VISA runtimes.

- **Auto-Detection & Custom Resource Paths**
  Automatically detects connected Rigol scopes by default VID/PID (`0x1AB1:0x0588`), or accepts explicit USB resource identifiers (`usbtmc://VIDxPID`).

- **Near Real-Time Screen Mirroring**
  Continuously streams the oscilloscope's display bitmap directly to your monitor with low latency, ideal for presentations, remote lab setups, and documentation.

- **Bi-Directional Channel & Timebase Control**
  - **CH1 Vertical Scale:** Switch volts/division dynamically (2 mV to 10 V).
  - **CH2 Enable/Disable & Vertical Scale:** Toggle Channel 2 on or off and configure its scale independently.
  - **Horizontal Timebase:** Switch seconds/division (5 ns to 50 s).
  - **Automatic State Synchronization:** Detects and reflects the instrument's active settings (CH1/CH2 scales, timebase, and CH2 visibility) as soon as the connection is established.

- **One-Click Screen Capture & Export**
  Export the live display to disk in standard formats (**PNG**, **JPEG**, or **BMP**) with automatically timestamped file names.

- **Thread-Safe Activity Log**
  Built-in monospace diagnostic log at the bottom of the window records all SCPI transactions, connection events, and instrument confirmations with second-level timestamps.

- **Thread-Safe SCPI Sequentialization**
  Guarantees zero-collision SCPI packet delivery using asynchronous semaphores (`SemaphoreSlim`), allowing concurrent screen-capture streaming and user-initiated parameter adjustments over a single USB pipe without interleaving errors.

---

## Target Hardware & Prerequisites

- **Supported Instruments:** Rigol DS1000E / DS1000D series (DS1052E, DS1102E, DS1052D, DS1102D).
- **Runtime:** .NET Framework 4.8 on Windows 10 / Windows 11.
- **Driver:** The oscilloscope USB device bound to the **WinUSB** driver (readily configured using [Zadig](https://zadig.akeo.ie/)).

---

## Tech Stack

- **Language:** C# 7.3+ / .NET Framework 4.8
- **UI Framework:** Windows Forms (WinForms)
- **Communications:** Direct WinUSB P/Invoke + USBTMC Subclass Specification (IEEE-488.2 / SCPI)
- **External Dependencies:** *None* (no external NuGet packages, no vendor DLLs)
