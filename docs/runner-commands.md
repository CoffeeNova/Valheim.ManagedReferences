# GitHub Actions Self-Hosted Runner Setup Guide


## Initial Setup


### 1. Create Runner User


```text
sudo adduser gh-runner
sudo usermod -aG sudo gh-runner
sudo su - gh-runner
```


### 2. Install GitHub Actions Runner


#### Create directory


```text
mkdir -p ~/actions-runner && cd ~/actions-runner
```


#### Download runner (get latest version from GitHub)


```text
# Example — replace with the latest version from GitHub Releases
wget https://github.com/actions/runner/releases/download/v2.330.0/actions-runner-linux-x64-2.330.0.tar.gz
tar xzf actions-runner-linux-x64-2.330.0.tar.gz
```


#### Configure runner


Get token from: GitHub → Settings → Actions → Runners → New self-hosted runner


```text
./config.sh --url https://github.com/YOUR_ORG/YOUR_REPO --token YOUR_TOKEN
```


#### During setup


- Runner group: [press Enter for Default]
- Runner name: ubuntu-valheim-runner
- Labels: ubuntu,valheim
- Work folder: [press Enter for _work]


### 3. Install as systemd Service (recommended)


GitHub recommends installing/managing the runner as a service via `svc.sh` (created after a successful `./config.sh`).[1]


#### Install and start service (via svc.sh)


```text
cd /home/gh-runner/actions-runner

# Install systemd unit
sudo ./svc.sh install

# Start service
sudo ./svc.sh start
```


#### Enable autostart


```text
# Find the exact unit name (depends on owner/repo/runner-name)
systemctl list-units | grep actions.runner

# Then enable autostart (example unit name):
sudo systemctl enable actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


### 4. IMPORTANT: Fix KillMode + prevent _diag conflicts


#### Why this matters


If the service stops uncleanly and leaves runner child processes behind, GitHub may return `A session for this runner already exists` (SessionConflict), and the runner may also leave stale diagnostic files under `_diag/pages`, which can later trigger errors like:

`Error: The file '/home/gh-runner/actions-runner/_diag/pages/...log' already exists.`

Setting `KillMode=control-group` ensures systemd kills all remaining processes in the unit’s control group on stop, not just the main process.[2]


#### Apply systemd override (fixes “left-over process” issue)


```text
sudo systemctl edit actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


Paste:


```text
[Service]
KillMode=control-group
```


Apply and restart:


```text
sudo systemctl daemon-reload
sudo systemctl restart actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


Verify:


```text
systemctl show -p KillMode actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
# Expected: KillMode=control-group
```


#### Optional: auto-clean _diag/pages + _work/_temp before service start


Runner diagnostics are stored under `_diag/`. Cleaning `_diag/pages` before each start can help prevent `...log already exists` issues.[3]


Edit override again:


```text
sudo systemctl edit actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


Add/replace with:


```text
[Service]
KillMode=control-group
ExecStartPre=/bin/bash -lc 'rm -rf /home/gh-runner/actions-runner/_diag/pages/* /home/gh-runner/actions-runner/_work/_temp/* || true'
```


Apply:


```text
sudo systemctl daemon-reload
sudo systemctl restart actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


### 5. (Optional) Manual service file (NOT recommended)


If you must create a unit manually: run the runner via `runsvc.sh` (not `run.sh`) and set `KillMode=control-group`. GitHub documents using `svc.sh` for systemd-based Linux systems.[1]


Service file path example:


```text
sudo nano /etc/systemd/system/actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


Example configuration:


```text
[Unit]
Description=GitHub Actions Runner (CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner)
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=/home/gh-runner/actions-runner/runsvc.sh
User=gh-runner
WorkingDirectory=/home/gh-runner/actions-runner
KillMode=control-group
KillSignal=SIGTERM
TimeoutStopSec=5min

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


#### Check status


```text
sudo systemctl status actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service --no-pager
```


#### Start runner


```text
sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


#### Stop runner


```text
sudo systemctl stop actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


#### Restart runner


```text
sudo systemctl restart actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


#### View logs in real-time


```text
sudo journalctl -u actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service -f
```


#### View recent logs


```text
sudo journalctl -u actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service -n 100 --no-pager
```


### Manual Run (for debugging)


Do not run `./run.sh` in parallel with the systemd service (it may cause session conflicts).


```text
cd ~/actions-runner
./run.sh

# Press Ctrl+C to stop
```


### Useful Aliases


```text
# Add to ~/.bashrc

echo 'alias runner-status="sudo systemctl status actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service --no-pager"' >> ~/.bashrc
echo 'alias runner-start="sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service"' >> ~/.bashrc
echo 'alias runner-stop="sudo systemctl stop actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service"' >> ~/.bashrc
echo 'alias runner-restart="sudo systemctl restart actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service"' >> ~/.bashrc
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
```

```test
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

### Install Valheim Dedicated Server


```text
# First-time login (requires Steam Guard code)
steamcmd +login YOUR_STEAM_USERNAME
```

```text

steamcmd +login YOUR_STEAM_USERNAME \
+force_install_dir /home/gh-runner/valheim_server \
+app_update 896660 validate \
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


```text
sudo nano /home/gh-runner/update-valheim.sh
```


```text
#!/bin/bash
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


### Fix: “The file '.../_diag/pages/...log' already exists”


This usually means stale files are left in:

`/home/gh-runner/actions-runner/_diag/pages/`

Note: if it fails at “Set up job”, workflow steps have not started yet, so cleanup must happen on the host (manual cleanup) or via systemd `ExecStartPre` as shown above.[4]


One-time cleanup:


```text
sudo systemctl stop actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

rm -rf /home/gh-runner/actions-runner/_diag/pages/* || true
rm -rf /home/gh-runner/actions-runner/_work/_temp/* || true

sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


### Fix: “A session for this runner already exists”


Make sure `KillMode=control-group` is applied, and make sure you are not running `./run.sh` in parallel.[2]


One-time “detox” if it is already stuck:


```text
sudo systemctl stop actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
sudo pkill -f Runner.Listener || true
sudo pkill -f run-helper.sh || true
sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


### Clean Runner Job Data (safe variant)


Avoid deleting the whole `_work` unless the runner is truly ephemeral; usually it is enough to clean `_diag/pages` and `_work/_temp`.[3]


```text
sudo systemctl stop actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service

rm -rf /home/gh-runner/actions-runner/_diag/pages/* || true
rm -rf /home/gh-runner/actions-runner/_work/_temp/* || true

sudo systemctl start actions.runner.CoffeeNova-Valheim.ManagedReferences.ubuntu-valheim-runner.service
```


### GLIBC Version Error


If you see errors about GLIBC_2.27 or GLIBC_2.28:

**Solution:** upgrade Ubuntu to 18.04+


```text
sudo do-release-upgrade
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
```



## Important Notes


1. **Steam Session**: SteamCMD saves login session for ~30 days. After first login with Steam Guard code, subsequent runs typically don't require Steam Guard again.

2. **Platform Type**: Always use `+@sSteamCmdForcePlatformType windows` for Valheim modding, even on Linux. Mods require Windows DLLs.

3. **Install Directory**: Use `+force_install_dir` to specify exact location. Without it, Steam uses a default Steam library path.

4. **Auto-start**: The systemd service with `After=network-online.target` ensures runner starts after VM reboot.

5. **Diagnostics**: Runner diagnostic logs are stored under the runner install directory in `_diag/`.[3]
