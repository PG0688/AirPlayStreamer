# Raspberry Pi AirPlay 2 Receiver Setup

This guide sets up a Raspberry Pi as an AirPlay 2 receiver using shairport-sync, enabling multi-room audio with HomePod and other AirPlay devices.

## Prerequisites

- Raspberry Pi (any model with audio output)
- Raspberry Pi OS (Bullseye or later)
- Speaker connected via 3.5mm jack, HDMI, or USB DAC

## Installation

### 1. Install NQPTP (Required for AirPlay 2)

```bash
sudo apt update
sudo apt install -y build-essential git autoconf automake libtool libpopt-dev libconfig-dev libasound2-dev avahi-daemon libavahi-client-dev libssl-dev libsoxr-dev libplist-dev libsodium-dev libavutil-dev libavcodec-dev libavformat-dev uuid-dev libgcrypt-dev xxd

cd ~
git clone https://github.com/mikebrady/nqptp.git
cd nqptp
autoreconf -fi
./configure --with-systemd-startup
make
sudo make install
sudo systemctl enable nqptp
sudo systemctl start nqptp
```

### 2. Build shairport-sync with AirPlay 2

```bash
cd ~
git clone https://github.com/mikebrady/shairport-sync.git
cd shairport-sync
autoreconf -fi
./configure --sysconfdir=/etc --with-alsa --with-soxr --with-avahi --with-ssl=openssl --with-systemd --with-airplay-2
make
sudo make install
sudo systemctl enable shairport-sync
```

### 3. Configure shairport-sync

Copy the configuration file from this repo:

```bash
sudo cp shairport-sync.conf /etc/shairport-sync.conf
```

Or edit manually:

```bash
sudo nano /etc/shairport-sync.conf
```

Key settings:
- `name = "Alexa"` - Device name shown in AirPlay list
- `ignore_volume_control = "yes"` - Keep volume at 100%
- `audio_backend_latency_offset_in_seconds = -0.1` - Sync with HomePod

### 4. Start the service

```bash
sudo systemctl restart shairport-sync
sudo systemctl status shairport-sync
```

## Usage

1. Open Control Center on iPhone/iPad/Mac
2. Long-press the audio output panel
3. Select both "HomePod" and "Alexa" (or your configured name)
4. Audio will play on both devices in sync

## Troubleshooting

### Device not appearing in AirPlay list

```bash
# Check service status
sudo systemctl status shairport-sync

# Check avahi/bonjour
avahi-browse -a

# Restart services
sudo systemctl restart avahi-daemon
sudo systemctl restart shairport-sync
```

### Audio out of sync

Adjust `audio_backend_latency_offset_in_seconds` in `/etc/shairport-sync.conf`:
- Negative values: audio plays earlier
- Positive values: audio plays later

### No audio output

```bash
# Test ALSA
aplay -l  # List devices
speaker-test -c 2  # Test speakers

# Check volume
alsamixer
```

## Connecting Bluetooth Speaker (Optional)

To route audio to a Bluetooth speaker like Echo Dot:

```bash
# Install Bluetooth tools
sudo apt install -y pulseaudio pulseaudio-module-bluetooth

# Pair device
bluetoothctl
> power on
> agent on
> scan on
> pair <MAC_ADDRESS>
> connect <MAC_ADDRESS>
> trust <MAC_ADDRESS>
> exit

# Set as default sink
pactl list sinks short
pactl set-default-sink <sink_name>
```

Note: Bluetooth routing adds latency and complexity. Direct AirPlay 2 is recommended.
