#!/usr/bin/env bash
# Read-only inventory. Does not install packages, flash hardware or expose tokens.
printf '\n--- Hardware / operating system ---\n'
if [ -r /proc/device-tree/model ]; then tr -d '\0' < /proc/device-tree/model; printf '\n'; fi
uname -m
if [ -r /etc/os-release ]; then cat /etc/os-release; fi
if [ -r /etc/nv_tegra_release ]; then head -n 1 /etc/nv_tegra_release; fi
printf '\n--- ROS environments ---\n'
printf 'ROS_DISTRO=%s\n' "${ROS_DISTRO:-unset}"
if [ -d /opt/ros ]; then ls -1 /opt/ros; fi
printf '\n--- Installed mapping packages ---\n'
if command -v dpkg-query >/dev/null; then
  dpkg-query -W -f='${Package} ${Version}\n' 'nvidia-jetpack' '*librealsense*' '*rtabmap*' '*realsense2-camera*' 2>/dev/null
fi
printf '\n--- Camera USB models (no serial numbers) ---\n'
if command -v lsusb >/dev/null; then lsusb | grep -Ei 'Intel|RealSense'; fi
printf '\n--- ROS package availability ---\n'
if command -v ros2 >/dev/null; then ros2 pkg list | grep -E '^(rtabmap|realsense2|sensor_msgs|rclpy)'; fi
printf '\n--- Clock ---\n'
date -u
