#!/usr/bin/env bash

set -euo pipefail

sudo apt-get update
sudo apt-get install -y wget apt-transport-https software-properties-common jq

source /etc/os-release

repository_package="packages-microsoft-prod.deb"
trap 'rm -f "$repository_package"' EXIT

wget -q "https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/${repository_package}"
sudo dpkg -i "$repository_package"

sudo apt-get update

if apt-cache show powershell >/dev/null 2>&1; then
	sudo apt-get install -y powershell
else
	machine_architecture="$(dpkg --print-architecture)"
	case "$machine_architecture" in
		amd64)
			archive_suffix="linux-x64.tar.gz"
			;;
		arm64)
			archive_suffix="linux-arm64.tar.gz"
			;;
		armhf)
			archive_suffix="linux-arm32.tar.gz"
			;;
		*)
			printf 'No PowerShell archive is available for architecture: %s\n' "$machine_architecture" >&2
			exit 1
			;;
	esac

	archive_directory="$(mktemp -d)"
	trap 'rm -rf "$archive_directory"' EXIT
	archive_url="$(wget -qO- https://api.github.com/repos/PowerShell/PowerShell/releases/latest | jq -r --arg suffix "$archive_suffix" '.assets[] | select(.name | endswith($suffix)) | .browser_download_url' | head -n 1)"

	if [[ -z "$archive_url" ]]; then
		printf 'Could not find the latest PowerShell archive for %s.\n' "$machine_architecture" >&2
		exit 1
	fi

	wget -qO "$archive_directory/powershell.tar.gz" "$archive_url"
	sudo install -d /opt/microsoft/powershell/7
	sudo tar -xzf "$archive_directory/powershell.tar.gz" -C /opt/microsoft/powershell/7
	sudo chmod +x /opt/microsoft/powershell/7/pwsh
	sudo ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh
fi

exec pwsh