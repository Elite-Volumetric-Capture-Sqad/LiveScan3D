﻿//   Copyright (C) 2015  Marek Kowalski (M.Kowalski@ire.pw.edu.pl), Jacek Naruniec (J.Naruniec@ire.pw.edu.pl)
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
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System.Globalization;
using System.Runtime.Serialization;

using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Timers;

using System.Diagnostics;


namespace KinectServer
{
    public partial class MainWindowForm : Form
    {
        [DllImport("ICP.dll")]
        static extern float ICP(IntPtr verts1, IntPtr verts2, int nVerts1, int nVerts2, float[] R, float[] t, int maxIter = 200);

        KinectServer oServer;
        TransferServer oTransferServer;

        //Those three variables are shared with the OpenGLWindow class and are used to exchange data with it.
        //Vertices from all of the sensors
        List<float> lAllVertices = new List<float>();
        //Color data from all of the sensors
        List<byte> lAllColors = new List<byte>();
        //Sensor poses from all of the sensors
        List<AffineTransform> lAllCameraPoses = new List<AffineTransform>();
        //Body data from all of the sensors
        List<Body> lAllBodies = new List<Body>();

        bool bServerRunning = false;
        bool bRecording = false;
        bool bSaving = false;

        //Live view open or not
        bool bLiveViewRunning = false;

        System.Timers.Timer oStatusBarTimer = new System.Timers.Timer();

        KinectSettings oSettings = new KinectSettings();

        //The live view window class
        OpenGLWindow oOpenGLWindow;
        public event EventHandler ResizeEnd;

        public MainWindowForm()
        {
            //This tries to read the settings from "settings.bin", if it failes the settings stay at default values.
            try
            {
                IFormatter formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                Stream stream = new FileStream("settings.bin", FileMode.Open, FileAccess.Read);
                oSettings = (KinectSettings)formatter.Deserialize(stream);
                stream.Close();
            }
            catch (Exception)
            {
            }

            oServer = new KinectServer(oSettings);
            oServer.eSocketListChanged += new SocketListChangedHandler(UpdateListView);
            oServer.eSocketListChanged += new SocketListChangedHandler(ClientConnectionChanged);
            oServer.SetMainWindowForm(this);
            oTransferServer = new TransferServer();
            oTransferServer.lVertices = lAllVertices;
            oTransferServer.lColors = lAllColors;
            InitializeComponent();
            UpdateSettingsButtonEnabled();//will disable settings button with no devices connected.
            SetButtonsForExport();

            //TODO: REMOVE TEST DATA
            //PostSync postSync = new PostSync();
            //List<ClientSyncData> testdata = PostSync.GetTestData();
            //PostSync.GenerateSyncList(testdata);

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            //The current settings are saved to a files.
            IFormatter formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

            Stream stream = new FileStream("settings.bin", FileMode.Create, FileAccess.Write);
            formatter.Serialize(stream, oSettings);
            stream.Close();

            oServer.StopServer();
            oTransferServer.StopServer();
        }

        private void ClientConnectionChanged(List<KinectSocket> socketList)
        {
            //Disable the temporal hardware sync if all clients disconnected
            if(socketList.Count == 0)
            {
                oServer.bTempHwSyncEnabled = false;

                if(oServer.fSettingsForm != null)
                {
                    oServer.fSettingsForm.BeginInvoke(new Action(() =>
                    {
                        oServer.fSettingsForm.DisableHardwareSyncButton();
                    }));
                }                
            }
        }

        //Starts the server
        private void btStart_Click(object sender, EventArgs e)
        {
            bServerRunning = !bServerRunning;

            if (bServerRunning)
            {
                oServer.StartServer();
                oTransferServer.StartServer();
                btStart.Text = "Stop server";
            }
            else
            {
                oServer.StopServer();
                oTransferServer.StopServer();
                btStart.Text = "Start server";
            }
        }

        //Opens the settings form
        private void btSettings_Click(object sender, EventArgs e)
        {
            if (oServer.GetSettingsForm() == null)
            {
                SettingsForm form = new SettingsForm();
                form.oSettings = oSettings;
                form.oServer = oServer;
                form.Show();
                oServer.SetSettingsForm(form);
            }
        }

        //Performs recording which is synchronized frame capture.
        //The frames are downloaded from the clients and saved once recording is finished.
        private void recordingWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            oServer.ClearStoredFrames();

            bool networkSyncEnabled = oSettings.bNetworkSync;
            BackgroundWorker worker = (BackgroundWorker)sender;

            oServer.SendAndConfirmPreRecordProcess();

            if (!networkSyncEnabled || oServer.bTempHwSyncEnabled)
            {
                oServer.SendCaptureFramesStart();

                Stopwatch counter = new Stopwatch();
                counter.Start();

                while (!worker.CancellationPending)
                {
                    SetStatusBarOnTimer("Recording: " + counter.Elapsed.Minutes.ToString("D2") + ":" + counter.Elapsed.Seconds.ToString("D2"), 5000);
                }

                oServer.SendCaptureFramesStop();

            }

            else if (networkSyncEnabled)
            {
                int nCaptured = 0;

                while (!worker.CancellationPending)
                {
                    oServer.CaptureSynchronizedFrame();
                    nCaptured++;
                    SetStatusBarOnTimer("Captured frame " + (nCaptured).ToString() + ".", 5000);
                }
            }

            oServer.SendAndConfirmPostRecordProcess();

        }

        private void recordingWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //After recording has been terminated it is time to begin sorting the frames.
            if (oServer.bTempHwSyncEnabled)
            {
                SetStatusBarOnTimer("Synchronizing recording. This could take some time", 5000);
                syncWorker.RunWorkerAsync();
            }

            //If sync wasn't enabled, we go straight to saving the files
            else
            {
                SetStatusBarOnTimer("Recording completed!", 2000);
                StartSaving();
            }

        }

        private void syncWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            //TODO: What if sync was not successfull?
            bool synced = true;

            Console.ReadLine();

            if (oServer.bTempHwSyncEnabled)
            {
                if (!oServer.GetTimestampLists())
                {
                    SetStatusBarOnTimer("Could not get Timestamp List. Saved recording is unsychronized!", 5000);
                    synced = false;
                }

                else
                {
                    if (!oServer.CreatePostSyncList())
                    {
                        SetStatusBarOnTimer("Could not match timestamps. Please check your Temporal Sync setup and Kinect firmware!", 5000);
                        synced = false;
                    }

                    else
                    {
                        if (!oServer.ReorderSyncFramesOnClient())
                        {
                            SetStatusBarOnTimer("Could not reorganize files for sync on at least one device!", 5000);
                            synced = false;
                        }

                        else
                        {
                            SetStatusBarOnTimer("Sync sucessfull", 5000);
                        }
                    }

                }
            }
        }

        private void syncWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            StartSaving();
        }

        //Opens the live view window
        private void OpenGLWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            bLiveViewRunning = true;
            oOpenGLWindow = new OpenGLWindow();

            //The variables below are shared between this class and the OpenGLWindow.
            lock (lAllVertices)
            {
                oOpenGLWindow.vertices = lAllVertices;
                oOpenGLWindow.colors = lAllColors;
                oOpenGLWindow.cameraPoses = lAllCameraPoses;
                oOpenGLWindow.bodies = lAllBodies;
                oOpenGLWindow.settings = oSettings;
            }
            oOpenGLWindow.Run();
        }

        private void OpenGLWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            bLiveViewRunning = false;
            updateWorker.CancelAsync();
        }

        private void StartSaving()
        {
            //Only store frames when capturing pointclouds
            if (oSettings.eExportMode == KinectSettings.ExportMode.Pointcloud)
            {
                bSaving = true;
                btRecord.Text = "Stop saving";
                btRecord.Enabled = true;

                savingWorker.RunWorkerAsync();
            }

            else
            {
                btRecord.Enabled = true;
                btRecord.Text = "Start recording";
            }
        }

        private void savingWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            //Saving is downloading the frames from clients and saving them locally.

            int nFrames = 0;

            //TODO: Get Take directory

            string outDir;

            if (oSettings.takePath != null)
            {
                outDir = oSettings.takePath;
            }

            else
            {
                return;
            }

            BackgroundWorker worker = (BackgroundWorker)sender;
            //This loop is running till it is either cancelled (using the btRecord button), or till there are no more stored frames.
            while (!worker.CancellationPending)
            {
                List<List<byte>> lFrameRGBAllDevices = new List<List<byte>>();
                List<List<float>> lFrameVertsAllDevices = new List<List<float>>();

                bool success = oServer.GetStoredFrame(lFrameRGBAllDevices, lFrameVertsAllDevices);

                //This indicates that there are no more stored frames.
                if (!success)
                    break;

                nFrames++;
                int nVerticesTotal = 0;
                for (int i = 0; i < lFrameRGBAllDevices.Count; i++)
                {
                    nVerticesTotal += lFrameVertsAllDevices[i].Count;
                }

                List<byte> lFrameRGB = new List<byte>();
                List<Single> lFrameVerts = new List<Single>();

                SetStatusBarOnTimer("Saving frame " + (nFrames).ToString() + ".", 5000);
                for (int i = 0; i < lFrameRGBAllDevices.Count; i++)
                {
                    lFrameRGB.AddRange(lFrameRGBAllDevices[i]);
                    lFrameVerts.AddRange(lFrameVertsAllDevices[i]);

                    //This is ran if the frames from each client are to be placed in separate files.
                    if (!oSettings.bMergeScansForSave)
                    {
                        string outputFilename = outDir + "\\" + nFrames.ToString().PadLeft(5, '0') + i.ToString() + ".ply";
                        Utils.saveToPly(outputFilename, lFrameVertsAllDevices[i], lFrameRGBAllDevices[i], oSettings.bSaveAsBinaryPLY);
                    }
                }

                //This is ran if the frames from all clients are to be placed in a single file.
                if (oSettings.bMergeScansForSave)
                {
                    string outputFilename = outDir + "\\" + nFrames.ToString().PadLeft(5, '0') + ".ply";
                    Utils.saveToPly(outputFilename, lFrameVerts, lFrameRGB, oSettings.bSaveAsBinaryPLY);
                }
            }

            oSettings.takePath = null;
        }

        private void savingWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            oServer.ClearStoredFrames();
            bSaving = false;

            //If the live view window was open, we need to restart the UpdateWorker.
            if (bLiveViewRunning)
                RestartUpdateWorker();

            btRecord.Enabled = true;
            btRecord.Text = "Start recording";
            btRefineCalib.Enabled = true;
            btCalibrate.Enabled = true;
        }

        //Continually requests frames that will be displayed in the live view window.
        private void updateWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            List<List<byte>> lFramesRGB = new List<List<byte>>();
            List<List<Single>> lFramesVerts = new List<List<Single>>();
            List<List<Body>> lFramesBody = new List<List<Body>>();

            BackgroundWorker worker = (BackgroundWorker)sender;
            while (!worker.CancellationPending)
            {
                Thread.Sleep(1);

                oServer.GetLatestFrame(lFramesRGB, lFramesVerts, lFramesBody);

                //Update the vertex and color lists that are common between this class and the OpenGLWindow.
                lock (lAllVertices)
                {
                    lAllVertices.Clear();
                    lAllColors.Clear();
                    lAllBodies.Clear();
                    lAllCameraPoses.Clear();

                    for (int i = 0; i < lFramesRGB.Count; i++)
                    {
                        lAllVertices.AddRange(lFramesVerts[i]);
                        lAllColors.AddRange(lFramesRGB[i]);
                        lAllBodies.AddRange(lFramesBody[i]);
                    }

                    lAllCameraPoses.AddRange(oServer.lCameraPoses);
                }

                //Notes the fact that a new frame was downloaded, this is used to estimate the FPS.
                if (oOpenGLWindow != null)
                    oOpenGLWindow.CloudUpdateTick();
            }
        }

        //Performs the ICP based pose refinement.
        private void refineWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            if (oServer.bAllCalibrated == false)
            {
                SetStatusBarOnTimer("Not all of the devices are calibrated.", 5000);
                return;
            }

            //Download a frame from each client.
            List<List<float>> lAllFrameVertices = new List<List<float>>();
            List<List<byte>> lAllFrameColors = new List<List<byte>>();
            List<List<Body>> lAllFrameBody = new List<List<Body>>();
            oServer.GetLatestFrame(lAllFrameColors, lAllFrameVertices, lAllFrameBody);

            //Initialize containers for the poses.
            List<float[]> Rs = new List<float[]>();
            List<float[]> Ts = new List<float[]>();
            for (int i = 0; i < lAllFrameVertices.Count; i++)
            {
                float[] tempR = new float[9];
                float[] tempT = new float[3];
                for (int j = 0; j < 3; j++)
                {
                    tempT[j] = 0;
                    tempR[j + j * 3] = 1;
                }

                Rs.Add(tempR);
                Ts.Add(tempT);
            }

            //Use ICP to refine the sensor poses.
            //This part is explained in more detail in our article (name on top of this file).

            for (int refineIter = 0; refineIter < oSettings.nNumRefineIters; refineIter++)
            {
                for (int i = 0; i < lAllFrameVertices.Count; i++)
                {
                    List<float> otherFramesVertices = new List<float>();
                    for (int j = 0; j < lAllFrameVertices.Count; j++)
                    {
                        if (j == i)
                            continue;
                        otherFramesVertices.AddRange(lAllFrameVertices[j]);
                    }

                    float[] verts1 = otherFramesVertices.ToArray();
                    float[] verts2 = lAllFrameVertices[i].ToArray();

                    IntPtr pVerts1 = Marshal.AllocHGlobal(otherFramesVertices.Count * sizeof(float));
                    IntPtr pVerts2 = Marshal.AllocHGlobal(lAllFrameVertices[i].Count * sizeof(float));

                    Marshal.Copy(verts1, 0, pVerts1, verts1.Length);
                    Marshal.Copy(verts2, 0, pVerts2, verts2.Length);

                    ICP(pVerts1, pVerts2, otherFramesVertices.Count / 3, lAllFrameVertices[i].Count / 3, Rs[i], Ts[i], oSettings.nNumICPIterations);

                    Marshal.Copy(pVerts2, verts2, 0, verts2.Length);
                    lAllFrameVertices[i].Clear();
                    lAllFrameVertices[i].AddRange(verts2);
                }
            }

            //Update the calibration data in client machines.
            List<AffineTransform> worldTransforms = oServer.lWorldTransforms;
            List<AffineTransform> cameraPoses = oServer.lCameraPoses;

            for (int i = 0; i < worldTransforms.Count; i++)
            {
                float[] tempT = new float[3];
                float[,] tempR = new float[3, 3];
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        tempT[j] += Ts[i][k] * worldTransforms[i].R[k, j];
                    }

                    worldTransforms[i].t[j] += tempT[j];
                    cameraPoses[i].t[j] += Ts[i][j];
                }

                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        for (int l = 0; l < 3; l++)
                        {
                            tempR[j, k] += Rs[i][l * 3 + j] * worldTransforms[i].R[l, k];
                        }

                        worldTransforms[i].R[j, k] = tempR[j, k];
                        cameraPoses[i].R[j, k] = tempR[j, k];
                    }
                }
            }

            oServer.lWorldTransforms = worldTransforms;
            oServer.lCameraPoses = cameraPoses;

            oServer.SendCalibrationData();
        }

        private void refineWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //Re-enable all of the buttons after refinement.
            btRefineCalib.Enabled = true;
            btCalibrate.Enabled = true;
            btRecord.Enabled = true;
        }

        //This is used for: starting/stopping the recording worker, stopping the saving worker
        private void btRecord_Click(object sender, EventArgs e)
        {
            if (oServer.nClientCount < 1)
            {
                SetStatusBarOnTimer("At least one client needs to be connected for recording.", 5000);
                return;
            }

            if (oServer.bTempHwSyncEnabled)
            {
                bool validConfig = true;

                if (!oServer.CheckTempHwSyncValid())
                {
                    SetStatusBarOnTimer("Temporal Hardware Sync Setup not valid! Please check arrangement", 5000);
                    return;
                }
            }

            //If we are saving frames right now, this button stops saving.
            if (bSaving)
            {
                btRecord.Enabled = false;
                savingWorker.CancelAsync();
                return;
            }

            bRecording = !bRecording;
            if (bRecording)
            {
                //Stop the update worker to reduce the network usage (provides better synchronization).
                updateWorker.CancelAsync();

                string takePath = oServer.CreateTakeDirectories(txtSeqName.Text);

                //Case: Server needs a path for storing values
                if (takePath != null && (oSettings.eExportMode == KinectSettings.ExportMode.Pointcloud || oSettings.eExtrinsicsFormat != KinectSettings.ExtrinsicsStyle.None))
                {
                    //Store path for the saving worker later
                    //TODO: How do to it without a global variable?
                    oSettings.takePath = takePath;
                }

                else if (takePath == null)
                {
                    SetStatusBarOnTimer("Error: Couldn't create take directory on either the server or the clients", 5000);
                    bRecording = false;
                    return;
                }

                //Store the camera extrinsics
                Utils.SaveExtrinsics(oSettings.eExtrinsicsFormat, takePath, oServer.GetClientSocketsCopy());

                recordingWorker.RunWorkerAsync();
                btRecord.Text = "Stop recording";
                btRefineCalib.Enabled = false;
                btCalibrate.Enabled = false;
            }
            else
            {
                btRecord.Enabled = false;
                recordingWorker.CancelAsync();
            }

        }

        private void btCalibrate_Click(object sender, EventArgs e)
        {
            if (oSettings.eExportMode == KinectSettings.ExportMode.Pointcloud)
            {
                oServer.Calibrate();
            }
        }

        public void SetCalibrateButtonActive(bool active)
        {
            btCalibrate.Enabled = active;
        }

        private void btRefineCalib_Click(object sender, EventArgs e)
        {
            if (oServer.nClientCount < 2)
            {
                SetStatusBarOnTimer("To refine calibration you need at least 2 connected devices.", 5000);
                return;
            }

            if (oSettings.eExportMode == KinectSettings.ExportMode.Pointcloud)
            {
                btRefineCalib.Enabled = false;
                btCalibrate.Enabled = false;
                btRecord.Enabled = false;

                refineWorker.RunWorkerAsync();
            }
        }

        public void SetRefineButtonActive(bool active)
        {
            btRefineCalib.Enabled = active;
        }

        void RestartUpdateWorker()
        {
            if (!updateWorker.IsBusy)
                updateWorker.RunWorkerAsync();
        }

        private void btShowLive_Click(object sender, EventArgs e)
        {
            if (oSettings.eExportMode == KinectSettings.ExportMode.Pointcloud)
            {
                RestartUpdateWorker();

                //Opens the live view window if it is not open yet.
                if (!OpenGLWorker.IsBusy)
                    OpenGLWorker.RunWorkerAsync();
            }
        }

        public void SetLiveButtonActive(bool active)
        {
            btShowLive.Enabled = active;
        }

        public void SetStatusBarOnTimer(string message, int milliseconds)
        {
            statusLabel.Text = message;

            oStatusBarTimer.Stop();
            oStatusBarTimer = new System.Timers.Timer();

            oStatusBarTimer.Interval = milliseconds;
            oStatusBarTimer.Elapsed += delegate (object sender, System.Timers.ElapsedEventArgs e)
            {
                oStatusBarTimer.Stop();
                statusLabel.Text = "";
            };
            oStatusBarTimer.Start();
        }

        //Updates the ListBox contaning the connected clients, called by events inside KinectServer.
        private void UpdateListView(List<KinectSocket> socketList)
        {
            List<string> listBoxItems = new List<string>();

            for (int i = 0; i < socketList.Count; i++)
                listBoxItems.Add(socketList[i].sSocketState);


            // Invoke UI logic on the same thread.
            lClientListBox.BeginInvoke(new Action(() =>
            {
                lClientListBox.DataSource = listBoxItems;
                UpdateSettingsButtonEnabled();
            }));

        }

        private void btKinectSettingsOpenButton_Click(object sender, EventArgs e)
        {
            if (lClientListBox.SelectedIndex == -1)
            {
                return;
            }
            //
            KinectConfigurationForm form = oServer.GetKinectSettingsForm(lClientListBox.SelectedIndex);
            if (form == null)
            {
                form = new KinectConfigurationForm();
            }
            //
            form.Configure(oServer, oSettings, lClientListBox.SelectedIndex);
            form.Show();
            oServer.SetKinectSettingsForm(lClientListBox.SelectedIndex, form);
        }

        /// <summary>
        /// Calibration, Refinement and Live view are only supported in Pointcloud mode.
        /// If we are in another mode, we disable the Main Window Buttons
        /// </summary>
        public void SetButtonsForExport()
        {
            bool pointCloudMode = oSettings.eExportMode == KinectSettings.ExportMode.Pointcloud;
            if (oServer.fMainWindowForm != null)
            {
                oServer.fMainWindowForm.SetCalibrateButtonActive(pointCloudMode);
                oServer.fMainWindowForm.SetLiveButtonActive(pointCloudMode);
                oServer.fMainWindowForm.SetRefineButtonActive(pointCloudMode);
            }

            foreach (var configForm in oServer.kinectSettingsForms.Values)
            {
                if (configForm != null)
                {
                    configForm.SetDepthFilterBoxActive(pointCloudMode);
                }
            }
        }

        private void UpdateSettingsButtonEnabled()
        {
            //Disable the deviceSettings button when no items are selected or no items could be selected.
            if (lClientListBox.SelectedIndex == -1 || lClientListBox.Items.Count == 0)
            {
                btKinectSettingsOpenButton.Enabled = false;
            }
            else
            {
                btKinectSettingsOpenButton.Enabled = true;
            }
        }

        private void lClientListBox_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
