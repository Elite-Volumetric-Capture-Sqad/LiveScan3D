#pragma once
#include <k4a/k4atypes.h>
#include <string>
#include "utils.h"
#include <fstream>
#include <filesystem>
#include <sstream>
//Kinect Configuration holds information that may be specific to kinects.
// It does not completely describe a configuration. See azureKinectCapture for that (which contains one of these)
// This is a subset of the kinect information that may need to set over the network.
// Particularly, the settings that may be unique from camera to camera.
//The struct handles serialization to and from bytes.
struct KinectConfiguration {
public:
	KinectConfiguration();
	KinectConfiguration(std::string serial);
	KinectConfiguration(std::string serialNo, std::string name, k4a_device_configuration_t conf, SYNC_STATE softwareSyncState, SYNC_STATE hardwareSyncState, int syncOffset, 
		int globalDeviceIndex, bool filterDepth, int filterDepthSize);
	std::string serialNumber;
	std::string nickname;
	k4a_device_configuration_t config;
	SYNC_STATE eSoftwareSyncState;
	SYNC_STATE eHardwareSyncState;
	int nSyncOffset;
	int nGlobalDeviceIndex;
	static const int byteLength = 42;//Expected length of the serialized form sent over the network. 
	bool filter_depth_map;
	int filter_depth_map_size = 5;
	char* ToBytes();
	void SetFromBytes(char* received);
	void Save();
	void TryLoad();

	void InitializeDefaults();
	int GetDepthCameraWidth();
	int GetDepthCameraHeight();
	int GetColorCameraWidth();
	int GetColorCameraHeight();
	void UpdateWidthAndHeight();
	void SetDepthMode(k4a_depth_mode_t depthMode);
	//todo: exposure
};