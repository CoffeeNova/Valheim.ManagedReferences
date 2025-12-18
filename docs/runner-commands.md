# GitHub Actions Self-Hosted Runner Setup Guide

## Initial Setup

### 1. Create Runner User

sudo adduser gh-runner
sudo usermod -aG sudo gh-runner
sudo su - gh-runner

### 2. Install GitHub Actions Runner

## Create directory

```text
mkdir actions-runner \&\& cd actions-runner
```

## Download runner (get latest version from GitHub)

```text
wget https://github.com/actions/runner/releases/download/v2.329.0/actions-runner-linux-x64-2.329.0.tar.gz
tar xzf actions-runner-linux-x64-2.329.0.tar.gz
```

## Configure runner

Get token from: GitHub → Settings → Actions → Runners → New self-hosted runner

```text
./config.sh --url https://github.com/YOUR_ORG/YOUR_REPO --token YOUR_TOKEN
```

## During setup

- Runner group: [press Enter for Default]

 Runner name: ubuntu-valheim-runner

- Labels: ubuntu,valheim

- Work folder: [press Enter for _work]

### 3. Install as systemd Service

Create/edit the service file:

```text
sudo nano /etc/systemd/system/actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

```

**Service Configuration:**

```text

[Unit]
Description=GitHub Actions Runner (CoffeeNova-Valheim.ubuntu-valheim-runner)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=gh-runner
WorkingDirectory=/home/gh-runner/actions-runner
ExecStart=/bin/bash -c "source /home/gh-runner/.profile \&\& /home/gh-runner/actions-runner/run.sh"
TimeoutStartSec=0
Restart=always
RestartSec=10
KillMode=process
KillSignal=SIGTERM

[Install]
WantedBy=multi-user.target

```

Enable and start:

```text

sudo systemctl daemon-reload
sudo systemctl enable actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

```

## Runner Management Commands

### Basic Operations

```text

cd ~/actions-runner

```

## Check status

```text

sudo systemctl status actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

```

## Start runner

```text

sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

```

## Stop runner

```text

sudo systemctl stop actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

```

## Restart runner

```text

sudo systemctl restart actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

```

## View logs in real-time

```text
sudo journalctl -u actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service -f
```

## View recent logs

```text

sudo journalctl -u actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service -n 100 --no-pager

```

### Manual Run (for debugging)

```text

cd ~/actions-runner
./run.sh

# Press Ctrl+C to stop

```

### Useful Aliases

```text

# Add to ~/.bashrc

echo 'alias runner-status="sudo systemctl status actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service"' >> ~/.bashrc
echo 'alias runner-start="sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service"' >> ~/.bashrc
echo 'alias runner-stop="sudo systemctl stop actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service"' >> ~/.bashrc
echo 'alias runner-logs="sudo journalctl -u actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service -f"' >> ~/.bashrc
source ~/.bashrc

```

## Valheim Setup for Modding

### Install .NET SDK 10

```text

wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0

# Add to PATH

echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
source ~/.bashrc

```

### Install SteamCMD

```text

sudo add-apt-repository multiverse
sudo apt update
sudo apt install steamcmd

```

### Install Valheim (Windows version for modding)

```text


# First-time login (requires Steam Guard code)

steamcmd +login YOUR_STEAM_USERNAME

# Install Valheim Windows version

steamcmd +@sSteamCmdForcePlatformType windows \
+login YOUR_STEAM_USERNAME \
+force_install_dir /home/gh-runner/valheim \
+app_update 892970 validate \
+quit

# Verify installation

ls -la /home/gh-runner/valheim/valheim_Data/Managed/

```

### Update Valheim (use in workflows)

```text

steamcmd +@sSteamCmdForcePlatformType windows \
+login YOUR_STEAM_USERNAME \
+force_install_dir /home/gh-runner/valheim \
+app_update 892970 validate \
+quit

```

## Automatic Valheim Updates

### Option 1: Update in Workflow (Recommended)

`.github/workflows/build.yml`:

```text

name: Build Valheim Mod

on:
push:
branches: [ main ]
workflow_dispatch:

jobs:
build:
runs-on: self-hosted

    steps:
      - uses: actions/checkout@v4
      
      - name: Update Valheim to latest
        run: |
          steamcmd +@sSteamCmdForcePlatformType windows \
            +login ${{ secrets.STEAM_USERNAME }} \
            +force_install_dir /home/gh-runner/valheim \
            +app_update 892970 validate \
            +quit
      
      - name: Sync managed references
        run: |
          dotnet run tools/sync-managed.cs \
            --managedPath /home/gh-runner/valheim/valheim_Data/Managed \
            --outDir lib/net46
      
      - name: Build
        run: dotnet build
    ```

### Option 2: Scheduled systemd Timer

Create update script:
```

sudo nano /home/gh-runner/update-valheim.sh

```text

\#!/bin/bash
set -e

LOG_FILE="/var/log/valheim-updater.log"
VALHEIM_DIR="/home/gh-runner/valheim"

echo "[$(date)] Starting Valheim update..." | tee -a "$LOG_FILE"

steamcmd +@sSteamCmdForcePlatformType windows \
+login YOUR_USERNAME \
+force_install_dir "$VALHEIM_DIR" \
  +app_update 892970 validate \
  +quit 2>&1 | tee -a "$LOG_FILE"

echo "[$(date)] Valheim update completed" | tee -a "$LOG_FILE"

```

Make executable:

```text

chmod +x /home/gh-runner/update-valheim.sh

```

Create service:

```text

sudo nano /etc/systemd/system/valheim-updater.service

```

```text

[Unit]
Description=Valheim Auto Updater
After=network-online.target

[Service]
Type=oneshot
User=gh-runner
ExecStart=/home/gh-runner/update-valheim.sh
StandardOutput=journal
StandardError=journal

```

Create timer:

```text

sudo nano /etc/systemd/system/valheim-updater.timer

```

```text

[Unit]
Description=Valheim Auto Updater Timer
Requires=valheim-updater.service

[Timer]
OnBootSec=5min
OnUnitActiveSec=6h
Persistent=true

[Install]
WantedBy=timers.target

```

Enable timer:

```text

sudo systemctl daemon-reload
sudo systemctl enable valheim-updater.timer
sudo systemctl start valheim-updater.timer

# Check status

sudo systemctl status valheim-updater.timer
sudo systemctl list-timers valheim-updater.timer

# View logs

sudo journalctl -u valheim-updater.service -f

```

## Troubleshooting

### Runner Won't Start After Reboot

```text

# Check if enabled

sudo systemctl is-enabled actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

# If disabled

sudo systemctl enable actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

# Check full logs

sudo journalctl -u actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service --since "today" --no-pager

```

### GLIBC Version Error

If you see errors about GLIBC_2.27 or GLIBC_2.28:

**Solution 1:** Update Ubuntu to 18.04+

```text

sudo do-release-upgrade

```

**Solution 2:** Use older runner version with Node.js 16

```text

cd ~/actions-runner
sudo systemctl stop actions.runner.*
./config.sh remove --token YOUR_REMOVAL_TOKEN

cd ~
rm -rf actions-runner
mkdir actions-runner \&\& cd actions-runner

wget <https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-linux-x64-2.311.0.tar.gz>
tar xzf actions-runner-linux-x64-2.311.0.tar.gz

./config.sh --url <https://github.com/YOUR_ORG/YOUR_REPO> --token YOUR_NEW_TOKEN

# Then reinstall service as described above

```

### Runner Starts Manually but Not via systemd

```text
# Test manual run first

cd ~/actions-runner
./run.sh

# If works, the issue is systemd configuration

# Make sure service file uses run.sh and loads environment

# ExecStart=/bin/bash -c "source /home/gh-runner/.profile \&\& /home/gh-runner/actions-runner/run.sh"

```

### Check Runner Status in GitHub

Go to: `https://github.com/YOUR_ORG/YOUR_REPO/settings/actions/runners`

Should show: ✅ **Idle** (green) when working

## GitHub Secrets Setup

Add to GitHub: Repository → Settings → Secrets and variables → Actions → New repository secret

Required secrets:

- `STEAM_USERNAME` - Your Steam account username

## Key Commands Reference

```text

# Check Ubuntu version

lsb_release -a

# Check GLIBC version

ldd --version

# Find Valheim installation

find ~ -type d -name "*alheim" 2>/dev/null

# Remove incorrectly installed Valheim

rm -rf ~/.steam/steamapps/common/Valheim
rm -f ~/.steam/steamapps/appmanifest_892970.acf

# Test SteamCMD login (saves session)

steamcmd +login YOUR_USERNAME +quit

# Force reinstall runner service

cd ~/actions-runner
sudo systemctl stop actions.runner.*
sudo ./svc.sh uninstall

# Edit service file as shown above

sudo systemctl daemon-reload
sudo systemctl enable actions.runner.*
sudo systemctl start actions.runner.*

```

## Important Notes

1. **Steam Session**: SteamCMD saves login session for ~30 days. After first login with Steam Guard code, subsequent runs don't require password.

2. **Platform Type**: Always use `+@sSteamCmdForcePlatformType windows` for Valheim modding, even on Linux. Mods require Windows DLLs.

3. **Install Directory**: Use `+force_install_dir` to specify exact location. Without it, Steam uses default location (~/.steam/steamapps/common/).

4. **Auto-start**: The systemd service with `After=network-online.target` ensures runner starts after VM reboot.

5. **Logs**: Always check `journalctl` for systemd service issues. For runner-specific issues, use `./run.sh` manually.
