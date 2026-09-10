"""RealSense aligned RGB-D -> 6DoF RTAB-Map. Requires installed ROS 2 packages.

No IMU is assumed: the user's D400 variant is not yet confirmed. Calibrate the
mount and use an IMU-supported configuration after confirming the exact model.
This is an integration launch, not a verified Jetson image or a flight controller.
"""
import os
from datetime import datetime, timezone
from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, IncludeLaunchDescription
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch.substitutions import LaunchConfiguration


def include(package, filename, arguments):
    return IncludeLaunchDescription(
        PythonLaunchDescriptionSource(os.path.join(get_package_share_directory(package), "launch", filename)),
        launch_arguments=arguments.items())


def generate_launch_description():
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d_%H%M%S_%f")
    # A new database avoids accidentally presenting a previous flight as new work.
    # No existing RTAB-Map database is deleted.
    database = os.path.expanduser("~/.ros/simurgh_" + stamp + ".db")
    return LaunchDescription([
        DeclareLaunchArgument("database_path", default_value=database),
        DeclareLaunchArgument("mapping_args", default_value="--Grid/Sensor 1 --Grid/3D true --Grid/CellSize 0.10 --Grid/RangeMax 3.0 --Reg/Force3DoF false"),
        include("realsense2_camera", "rs_launch.py", {
            "camera_namespace": "", "camera_name": "camera",
            "enable_color": "true", "enable_depth": "true",
            "align_depth.enable": "true", "enable_sync": "true",
            "enable_gyro": "false", "enable_accel": "false",
        }),
        include("rtabmap_launch", "rtabmap.launch.py", {
            "namespace": "rtabmap", "frame_id": "camera_link",
            "use_sim_time": "false", "stereo": "false", "depth": "true",
            "subscribe_rgbd": "false", "visual_odometry": "true",
            "rgb_topic": "/camera/color/image_raw",
            "depth_topic": "/camera/aligned_depth_to_color/image_raw",
            "camera_info_topic": "/camera/color/camera_info",
            "approx_sync": "true", "approx_sync_max_interval": "0.03",
            "wait_imu_to_init": "false", "rtabmap_viz": "false", "rviz": "false",
            "database_path": LaunchConfiguration("database_path"),
            "args": LaunchConfiguration("mapping_args"),
        }),
    ])
