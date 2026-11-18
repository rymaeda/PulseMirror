# PulseMirror

**PulseMirror** is a lightweight, zero-dependency Windows desktop client for real-time screen mirroring, remote control, and screen capture of Rigol digital oscilloscopes, specifically targeting the popular **DS1000E series** (such as the **DS1052E** and **DS1102E**).

Unlike typical bench tools, PulseMirror talks directly to the instrument through the standard **USBTMC** (USB Test and Measurement Class) protocol over **WinUSB**. There is no need for heavyweight vendor runtimes such as NI-VISA, Keysight IO Libraries Suite, or UltraSigma.

---

## Table of Contents

- [Key Features](#key-features)
- [Requirements](#requirements)
- [Getting Started](#getting-started)
- [Using PulseMirror](#using-pulsemirror)
- [Troubleshooting](#troubleshooting)
- [Tech Stack](#tech-stack)
- [For Contributors](#for-contributors)
- [Contributing](#contributing)

---

## Key Features

- **Direct USBTMC over WinUSB**
  Pure C# implementation of USBTMC framing (Bulk-OUT/Bulk-IN packets) on top of Microsoft's native `winusb.dll`. No proprietary VISA runtime is needed.

- **Auto-Detection and Custom Resource Paths**
  Detects connected Rigol scopes by the default VID/PID (`0x1AB1:0x0588`), or accepts an explicit identifier in the form `usbtmc://VIDxPID`.

- **Near Real-Time Screen Mirroring**
  Continuously streams the oscilloscope's display bitmap to your monitor. Useful for presentations, remote lab setups, and documentation.

- **Bi-Directional Channel and Timebase Control**
  - **CH1 / CH2 enable and vertical scale:** from 2 mV/div to 10 V/div.
  - **Vertical offset:** adjustable per channel.
  - **Horizontal timebase:** from 2 ns/div to 5 s/div.
  - **Automatic state synchronization:** the window reflects the instrument's current settings as soon as the connection is established.

- **Auto-Adjust Buttons (CH1 / CH2)**
  Reads the channel's measurements (`Vpp`, `Vmax`, `Vmin`, `Period`) and automatically adjusts V/div, offset, and s/div so the waveform is centered and fills most of the screen.

- **SCPI Measurements Panel**
  Reads measurements from the instrument for both channels (voltage, frequency, period, rise/fall time, widths, duty cycles, and more). Each one can be selected individually or all at once with **Read all**.

- **Trigger Controls**
  View and change the trigger type, source, and mode.

- **One-Click Screen Capture and Export**
  Saves the live display as **PNG**, **JPEG**, or **BMP** with a timestamped default file name.

- **Activity Log**
  A selectable, copyable log at the bottom of the window records connection events, instrument confirmations, and errors.

- **Safe SCPI Sequencing**
  All instrument access is serialized with a `SemaphoreSlim`. Screen streaming and user actions share the same USB pipe without interleaving errors.

---

## Requirements

- **Instrument:** Rigol DS1000E / DS1000D series (DS1052E, DS1102E, DS1052D, DS1102D).
- **Operating system:** Windows 10 or Windows 11.
- **Runtime:** .NET Framework 4.8.
- **Driver:** the oscilloscope's USB device must be bound to the **WinUSB** driver (see below).

---

## Getting Started

### 1. Install the WinUSB driver

1. Connect the oscilloscope to your PC with a USB cable.
2. Open [Zadig](https://zadig.akeo.ie/).
3. Select the Rigol device (VID `1AB1`, PID `0588`). Enable **Options > List All Devices** if it is not listed.
4. Choose **WinUSB** as the target driver and click **Replace Driver**.

> **Note:** replacing the driver stops other tools that depend on the vendor driver (such as UltraSigma) from using the instrument until you restore it.

### 2. Build and run

1. Clone the repository:

2. Open the solution in **Visual Studio**.
3. Build and run (**F5**). There are no NuGet packages to restore.

### 3. Connect

1. Leave the default resource (`usbtmc://1AB1x0588`) or enter your own.
2. Click **Connect**. The status label shows the model and serial number when the connection succeeds.

---

## Using PulseMirror

| Area | What it does |
|------|--------------|
| **Screen** | Live mirror of the oscilloscope display. |
| **Channel controls** | Enable CH1/CH2, set V/div and offset, and set the shared timebase. |
| **Auto CH1 / Auto CH2** | Measures the channel and adjusts V/div, offset, and s/div for a good waveform display. |
| **Measurements (SCPI)** | Tick the measurements you want. Values are refreshed about once per second for CH1 and CH2. |
| **Trigger** | Select the trigger type, source, and mode. |
| **Save Image...** | Saves the current screen as PNG, JPEG, or BMP. |
| **Messages** | Log of connection events, confirmations, and errors. |

### How Auto-Adjust works

- **V/div:** the smallest standard scale where the signal occupies about 6 of the 8 vertical divisions.
- **Offset:** `-(Vmax + Vmin) / 2`, which centers the waveform.
- **s/div:** the smallest standard scale that shows about 3 periods across the 12 horizontal divisions.
- The process repeats up to 3 times, because measurements from a clipped waveform are unreliable.
- The timebase is shared by both channels, so the last button pressed sets it.

---

## Troubleshooting

- **The device is not found:** check that the WinUSB driver is bound (Zadig) and that no other application is using the instrument.
- **Connection fails:** confirm the VID/PID in the resource field matches your device.
- **A measurement shows `-` or `error`:** the channel may be off, or there may be no signal to measure.
- **Instrument stays in "Rmt" state:** PulseMirror releases the front-panel lock after connecting. If the lock persists, press the **Force** key on the instrument.

---

## Tech Stack

- **Language:** C# / .NET Framework 4.8
- **UI:** Windows Forms
- **Communication:** WinUSB P/Invoke, USBTMC, SCPI (IEEE 488.2)
- **External dependencies:** none (no NuGet packages, no vendor DLLs)

---

## For Contributors

### Architecture

The UI only knows `IOscilloscope`. The driver translates calls into SCPI commands and sends them through an `IScpiTransport`. This makes it possible to support other instruments or transports without touching the UI.

### Project artifacts

| Path | Purpose |
|------|---------|
| `Program.cs` | Application entry point. |
| `Form1.cs` / `Form1.Designer.cs` | Main window. Controls are created and positioned in code. |
| `Controls/TriggerPanel.cs` | Trigger settings panel. |
| `Instruments/IOscilloscope.cs` | Instrument abstraction used by the UI. |
| `Instruments/RigolDS1000EService.cs` | SCPI driver for the DS1000E series. Serializes access with `SemaphoreSlim`. |
| `Instruments/OscilloscopeFactory.cs` | Creates and connects the instrument service. |
| `Instruments/IScpiTransport.cs` | Transport abstraction (write, read line, read block). |
| `Instruments/ScpiTransportFactory.cs` | Creates the transport (USBTMC only for now). |
| `Instruments/UsbTmc/UsbTmcTransport.cs` | USBTMC message framing on top of WinUSB. |
| `WinUsbDevice.cs` | Managed wrapper over WinUSB. |
| `Instruments/NativeMethods.cs` | P/Invoke declarations. |
| `Instruments/UsbDeviceFinder.cs` | USB device discovery by VID/PID. |
| `Instruments/MeasurementQuantity.cs` | Measurements that can be read via SCPI. |
| `Instruments/TriggerType.cs`, `TriggerSource.cs`, `TriggerMode.cs`, `TriggerSettings.cs` | Trigger model types. |

### Extension points

- **Add a measurement:** add a member to `MeasurementQuantity`, then map it in `GetMeasurementMnemonic` in `RigolDS1000EService`. Add its unit and format in `Form1`.
- **Support another instrument:** implement `IOscilloscope` (and reuse `IScpiTransport`), then register it in `OscilloscopeFactory`.
- **Add a transport:** implement `IScpiTransport` and return it from `ScpiTransportFactory`.

### Conventions

- All instrument access goes through `IOscilloscope`. The UI must not touch the transport directly.
- Instrument calls are asynchronous. Keep the UI thread free and update controls only on the UI thread.
- Use the `_isSyncingControls` guard when updating controls from instrument values, so the change is not sent back to the instrument.

---

## Contributing

Contributions are welcome.

1. Fork the repository.
2. Create a feature branch: `git checkout -b feature/my-feature`.
3. Commit your changes with clear messages.
4. Push the branch and open a **pull request** describing what changed and how you tested it, ideally with the instrument model you used.

Bug reports and feature requests are welcome in the **Issues** tab. Please include your instrument model, firmware version, and the contents of the message log.

---

## License

No license file is present in the repository yet. Add a `LICENSE` file (for example MIT) before accepting outside contributions, and state it here.

---

## Disclaimer

PulseMirror is an independent project and is not affiliated with or endorsed by RIGOL Technologies. All product names are trademarks of their respective owners.