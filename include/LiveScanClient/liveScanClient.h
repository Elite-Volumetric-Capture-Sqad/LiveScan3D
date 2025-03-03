//   Copyright (C) 2015  Marek Kowalski (M.Kowalski@ire.pw.edu.pl), Jacek Naruniec (J.Naruniec@ire.pw.edu.pl)
//   License: MIT Software License   See LICENSE.txt for the full license.

//   If you use this software in your research, then please use the following citation:

//    Kowalski, M.; Naruniec, J.; Daniluk, M.: "LiveScan3D: A Fast and Inexpensive 3D Data
//    Acquisition System for Multiple Kinect v2 Sensors". in 3D Vision (3DV), 2015 International Conference on, Lyon, France, 2015

//    @INPROCEEDINGS{Kowalski15,
//        author={Kowalski, M. and Naruniec, J. and Daniluk, M.},
//        booktitle={3D Vision (3DV), 2015 International Conference on},
//        title={LiveScan3D: A Fast and Inexpensive 3D Data Acquisition System for Multiple Kinect v2 Sensors},
//        year={2015},
//    }
#pragma once
#include "SocketCS.h" //Should always be on top, otherwise lots of definition errors
#include <thread>
#include <mutex>
#include <chrono>
#include <fstream>
#include "resource.h"
#include <KinectConfiguration.h>
#include "ImageRenderer.h"
#include "calibration.h"
#include "azureKinectCaptureVirtual.h"
#include "frameFileWriterReader.h"
#include "zstd.h"
#include "filter.h"


enum CLIENT_STATUS
{
	STATUS_STARTING,
	STATUS_CAMERA_ERROR,
	STATUS_RUNNING,
	STATUS_TERMINATED_NORMALLY,
	STATUS_CRASH
};

typedef struct StatusMessage
{
	std::wstring message;
	int time;
	bool priority;
};

typedef struct DeviceStatus
{
	CLIENT_STATUS status;
	std::string name;
};

class LiveScanClient
{
public:
    LiveScanClient();
    ~LiveScanClient();

	void RunClient(Log* logger, bool virtualDevice);
	void CloseClient();
	bool Connect(std::string ip);
	bool Disconnect();
	void SetClientActive(bool active);
	void SetPreviewMode(bool depth);

	//Below are thread-safe functions to get different resources from this client
	float GetFPSTS();
	PreviewFrame GetColorTS();
	PreviewFrame GetDepthTS();
	StatusMessage GetStatusMessageTS();
	bool GetConnectedTS();
	DeviceStatus GetDeviceStatusTS();

	std::mutex m_mRunning;
	std::mutex m_mPreviewResources;
	std::mutex m_mFPS;
	std::mutex m_mStatus;
	std::mutex m_mSocketThread;

private:
	Calibration calibration;

	bool m_bRunning;
	bool m_bConnected;
	bool m_bSocketThread;
	bool m_bVirtualDevice;
	bool m_bCalibrate;
	bool m_bCaptureFrames;
	bool m_bCaptureSingleFrame;
	bool m_bCapturing;
	bool m_bStartPreRecordingProcess;
	bool m_bConfirmPreRecordingProcess;
	bool m_bStartPostRecordingProcess;
	bool m_bConfirmPostRecordingProcess;
	bool m_bSaveCalibration;
	bool m_bConfirmCaptured;
	bool m_bSendCalibration;
	bool m_bCloseCamera;
	bool m_bConfirmCameraClosed;
	bool m_bStartCamera;
	bool m_bConfirmCameraInitialized;
	bool m_bCameraError;
	bool m_bUpdateSettings;
	bool m_bUpdateFilters;
	bool m_bRequestConfiguration;
	bool m_bSendConfiguration;
	bool m_bSendTimeStampList;
	bool m_bPostSyncedListReceived;
	bool m_bShowPreviewDuringRecording;
	bool m_bPreviewDisabled;
	bool m_bRequestLiveFrame;
	bool m_bShowDepth;
	bool m_bActiveClient;

	bool m_bFrameCompression;
	int m_iCompressionLevel;

	bool m_bAutoExposureEnabled;
	int m_nExposureStep;

	bool m_bAutoWhiteBalanceEnabled;
	int m_nKelvin;

	std::chrono::milliseconds m_tFrameTime;
	std::chrono::milliseconds m_tOldFrameTime;

	int m_nFPSFrameCounter;
	long m_nFPSUpdateCounter = 0;
	float m_fAverageFPS;

	ICapture* pCapture;
	KinectConfiguration configuration;
	CAPTURE_MODE m_eCaptureMode;

	int m_nExtrinsicsStyle;

	FrameFileWriterReader* m_framesFileWriterReader;

	SocketClient *m_pClientSocket;
	std::string m_sReceived;
	char byteToSend;
	std::string m_sLastUsedIP;
	bool m_bConnectionResult;

	std::vector<float> m_vBounds;

	Point3s* m_vLastFrameVertices;
	int m_vLastFrameVerticesSize;
	RGBA* m_vLastFrameRGB;

	std::vector<int> m_vFrameCount;
	std::vector<uint64_t> m_vFrameTimestamps;
	std::vector<int> m_vFrameID;
	std::vector<int> m_vPostSyncedFrameID;

	LogBuffer logBuffer;
	Log* log;

	int m_nFrameIndex;

	Point3f* m_pCameraSpaceCoordinates;
	RGBA* m_pColorPreview;
	RGBA* m_pDepthPreview;
	int m_nPreviewWidth;
	int m_nPreviewHeight;
	Point3f* m_pAllVertices;
	int m_nAllVerticesSize;
   
	//Image Resources
	std::vector<uchar> emptyJPEGBuffer;

	StatusMessage statusMessage;
	bool m_bNewMessage;
	CLIENT_STATUS m_eClientStatus;



	void UpdateFrame();
	void UpdatePreview();
	void SaveRawFrame();
	void SavePointcloudFrame(uint64_t timeStamp);
	void Calibrate();
	void SetStatusMessage(std::wstring message, int time, bool priority);
	void HandleSocket();
	bool StartCamera();
	void StopCamera();
	void DisposeDevice();
	void SendPostSyncConfirmation(bool success);
	void SendFrame(Point3s* vertices,int verticesSize, RGBA* RGB, bool live);
	bool PostSyncPointclouds();
	bool PostSyncRawFrames();

	void SocketThreadFunction();
	void StoreFrame(k4a_image_t pointCloudImage, cv::Mat *colorInDepth);
	void UpdateFPS();

	//Turbo Rainbow Color Map by Google, Copyright 2019 Google LLC., SPDX-License-Identifier: Apache-2.0, Author: Anton Mikhailov
	//Modified a bit so that all values are cramped into half the range, to have a better readability on close distances (up to 5m) where most of the action happens
	unsigned char rainbowLookup[256][3] = { {48,18,59},{51,24,74},{53,30,88},{55,36,102},{57,42,115},{59,47,128},{61,53,139},{63,59,151},{64,64,162},{65,70,172},{66,75,181},{68,81,191},{68,86,199},{69,92,207},{70,97,214},{70,102,221},{70,107,227},{71,113,233},{71,118,238},{71,123,242},{70,128,246},{70,133,250},{69,138,252},{68,143,254},{66,148,255},{64,153,255},{61,158,254},{58,163,252},{55,168,250},{51,173,247},{47,178,244},{44,183,240},{40,188,235},{37,192,231},{34,197,226},{31,201,221},{28,205,216},{26,210,210},{25,213,205},{24,217,200},{24,221,194},{24,224,189},{25,227,185},{28,230,180},{31,233,175},{34,235,170},{39,238,164},{44,240,158},{50,242,152},{56,244,145},{63,246,138},{70,248,132},{78,249,125},{85,250,118},{93,252,111},{101,253,105},{109,254,98},{117,254,92},{125,255,86},{132,255,81},{139,255,75},{146,255,71},{153,254,66},{159,253,63},{164,252,60},{169,251,57},{175,250,55},{180,248,54},{185,246,53},{190,244,52},{195,241,52},{200,239,52},{205,236,52},{210,233,53},{215,229,53},{219,226,54},{223,223,55},{227,219,56},{231,215,57},{235,211,57},{238,207,58},{241,203,58},{244,199,58},{246,195,58},{248,190,57},{250,186,57},{251,182,55},{252,177,54},{253,172,52},{254,167,50},{254,161,48},{254,155,45},{254,150,43},{254,144,41},{253,138,38},{252,132,35},{251,126,33},{249,120,30},{248,114,28},{246,108,25},{244,102,23},{242,96,20},{240,91,18},{237,85,16},{235,80,14},{232,75,12},{229,71,11},{226,67,10},{223,63,8},{220,59,7},{216,55,6},{212,51,5},{208,47,5},{204,43,4},{200,40,3},{195,37,3},{190,33,2},{185,30,2},{180,27,1},{175,24,1},{169,22,1},{164,19,1},{158,16,1},{152,14,1},{149,13,1},{146,11,1},{142,10,1},{139,9,2},{136,8,2},{133,7,2},{129,6,2},{126,5,2},{122,4,3},{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3} ,{122,4,3}};
};



