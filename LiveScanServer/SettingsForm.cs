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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;

namespace KinectServer
{
    public partial class SettingsForm : Form
    {
        public KinectSettings oSettings;
        public KinectServer oServer;

        private Timer scrollTimer = null;

        bool bFormLoaded = false;
        bool settingsChanged = false;

        public SettingsForm()
        {
            InitializeComponent();
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            txtMinX.Text = oSettings.aMinBounds[0].ToString(CultureInfo.InvariantCulture);
            txtMinY.Text = oSettings.aMinBounds[1].ToString(CultureInfo.InvariantCulture);
            txtMinZ.Text = oSettings.aMinBounds[2].ToString(CultureInfo.InvariantCulture);

            txtMaxX.Text = oSettings.aMaxBounds[0].ToString(CultureInfo.InvariantCulture);
            txtMaxY.Text = oSettings.aMaxBounds[1].ToString(CultureInfo.InvariantCulture);
            txtMaxZ.Text = oSettings.aMaxBounds[2].ToString(CultureInfo.InvariantCulture);

            lisMarkers.DataSource = oSettings.lMarkerPoses;

            cbCompressionLevel.SelectedText = oSettings.iCompressionLevel.ToString();

            chMerge.Checked = oSettings.bMergeScansForSave;
            txtICPIters.Text = oSettings.nNumICPIterations.ToString();
            txtRefinIters.Text = oSettings.nNumRefineIters.ToString();

            if (oServer != null)
                chHardwareSync.Checked = oServer.bTempHwSyncEnabled;
            else
                chHardwareSync.Checked = false;

            chNetworkSync.Checked = oSettings.bNetworkSync;

            chAutoExposureEnabled.Checked = oSettings.bAutoExposureEnabled;
            trManualExposure.Value = oSettings.nExposureStep;

            cbExtrinsicsFormat.SelectedIndex = (int)oSettings.eExtrinsicsFormat;

            rExportPointcloud.Checked = oSettings.eExportMode == KinectSettings.ExportMode.Pointcloud ? true : false;
            rExportRawFrames.Checked = !rExportPointcloud.Checked;

            cbEnablePreview.Checked = oSettings.bPreviewEnabled;


            if (oSettings.bSaveAsBinaryPLY)
            {
                rBinaryPly.Checked = true;
                rAsciiPly.Checked = false;
            }
            else
            {
                rBinaryPly.Checked = false;
                rAsciiPly.Checked = true;
            }

            lApplyWarning.Text = "";
            btApplyAllSettings.Enabled = false;

            bFormLoaded = true;
        }

        void UpdateClients()
        {
            if (bFormLoaded && !UpdateClientsBackgroundWorker.IsBusy)
            {
                Cursor.Current = Cursors.WaitCursor;

                oServer.SendSettings();

                //Check if we need to restart the cameras

                //TODO: Currently, the UI doesn't update as it stalls the thread. How can I get this to work without stalling it?

                Log.LogDebug("Updating settings on clients");

                if (chHardwareSync.Checked != oServer.bTempHwSyncEnabled)
                {
                    if (chHardwareSync.Checked)
                    {
                        chHardwareSync.Checked = oServer.EnableTemporalSync();
                        Log.LogDebug("Hardware sync is set on by the user");
                    }

                    else
                    {
                        chHardwareSync.Checked = !oServer.DisableTemporalSync();
                        Log.LogDebug("Hardware sync is set off by the user");

                    }
                }

                Cursor.Current = Cursors.Default;

                btApplyAllSettings.Enabled = false;
                lApplyWarning.Text = "";
            }
        }

        void SettingsChanged()
        {
            settingsChanged = true;
            UpdateApplyButton();
        }

        void UpdateApplyButton()
        {
            if (settingsChanged)
            {
                btApplyAllSettings.Enabled = true;
                lApplyWarning.Text = "Changed settings are not yet applied!";
            }
        }

        void UpdateMarkerFields()
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];

                float X, Y, Z;
                pose.GetOrientation(out X, out Y, out Z);

                txtOrientationX.Text = X.ToString(CultureInfo.InvariantCulture);
                txtOrientationY.Text = Y.ToString(CultureInfo.InvariantCulture);
                txtOrientationZ.Text = Z.ToString(CultureInfo.InvariantCulture);

                txtTranslationX.Text = pose.pose.t[0].ToString(CultureInfo.InvariantCulture);
                txtTranslationY.Text = pose.pose.t[1].ToString(CultureInfo.InvariantCulture);
                txtTranslationZ.Text = pose.pose.t[2].ToString(CultureInfo.InvariantCulture);

                txtId.Text = pose.id.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                txtOrientationX.Text = "";
                txtOrientationY.Text = "";
                txtOrientationZ.Text = "";

                txtTranslationX.Text = "";
                txtTranslationY.Text = "";
                txtTranslationZ.Text = "";

                txtId.Text = "";
            }
        }

        public void SetExposureControlsToManual(bool manual)
        {
            chAutoExposureEnabled.Enabled = !manual;
            trManualExposure.Enabled = manual;
            trManualExposure.Value = -5;
            chAutoExposureEnabled.CheckState = CheckState.Unchecked;
        }

        public void DisableHardwareSyncButton()
        {
            if (chHardwareSync.Checked)
            {
                chHardwareSync.Checked = false;
                chNetworkSync.Enabled = true;
                UpdateClients();
            }

        }

        private void txtMinX_TextChanged(object sender, EventArgs e)
        {
            Single.TryParse(txtMinX.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out oSettings.aMinBounds[0]);
            SettingsChanged();
        }

        private void txtMinY_TextChanged(object sender, EventArgs e)
        {
            Single.TryParse(txtMinY.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out oSettings.aMinBounds[1]);
            SettingsChanged();
        }

        private void txtMinZ_TextChanged(object sender, EventArgs e)
        {
            Single.TryParse(txtMinZ.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out oSettings.aMinBounds[2]);
            SettingsChanged();
        }

        private void txtMaxX_TextChanged(object sender, EventArgs e)
        {
            Single.TryParse(txtMaxX.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out oSettings.aMaxBounds[0]);
            SettingsChanged();
        }

        private void txtMaxY_TextChanged(object sender, EventArgs e)
        {
            Single.TryParse(txtMaxY.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out oSettings.aMaxBounds[1]);
            SettingsChanged();
        }

        private void txtMaxZ_TextChanged(object sender, EventArgs e)
        {
            Single.TryParse(txtMaxZ.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out oSettings.aMaxBounds[2]);
            SettingsChanged();
        }

        private void txtICPIters_TextChanged(object sender, EventArgs e)
        {
            Int32.TryParse(txtICPIters.Text, out oSettings.nNumICPIterations);
        }

        private void txtRefinIters_TextChanged(object sender, EventArgs e)
        {
            Int32.TryParse(txtRefinIters.Text, out oSettings.nNumRefineIters);
        }

        private void chMerge_CheckedChanged(object sender, EventArgs e)
        {
            oSettings.bMergeScansForSave = chMerge.Checked;
        }

        private void btAdd_Click(object sender, EventArgs e)
        {
            lock (oSettings)
                oSettings.lMarkerPoses.Add(new MarkerPose());
            lisMarkers.SelectedIndex = oSettings.lMarkerPoses.Count - 1;
            UpdateMarkerFields();
            SettingsChanged();
        }
        private void btRemove_Click(object sender, EventArgs e)
        {
            if (oSettings.lMarkerPoses.Count > 0)
            {
                oSettings.lMarkerPoses.RemoveAt(lisMarkers.SelectedIndex);
                lisMarkers.SelectedIndex = oSettings.lMarkerPoses.Count - 1;
                UpdateMarkerFields();
                SettingsChanged();
            }
        }

        private void lisMarkers_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateMarkerFields();
        }

        private void txtOrientationX_TextChanged(object sender, EventArgs e)
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];
                float X, Y, Z;
                pose.GetOrientation(out X, out Y, out Z);
                Single.TryParse(txtOrientationX.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out X);

                pose.SetOrientation(X, Y, Z);
                SettingsChanged();
            }
        }

        private void txtOrientationY_TextChanged(object sender, EventArgs e)
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];
                float X, Y, Z;
                pose.GetOrientation(out X, out Y, out Z);
                Single.TryParse(txtOrientationY.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out Y);

                pose.SetOrientation(X, Y, Z);
                SettingsChanged();
            }
        }

        private void txtOrientationZ_TextChanged(object sender, EventArgs e)
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];
                float X, Y, Z;
                pose.GetOrientation(out X, out Y, out Z);
                Single.TryParse(txtOrientationZ.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out Z);

                pose.SetOrientation(X, Y, Z);
                SettingsChanged();
            }
        }

        private void txtTranslationX_TextChanged(object sender, EventArgs e)
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                float X;
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];
                Single.TryParse(txtTranslationX.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out X);

                pose.pose.t[0] = X;
                SettingsChanged();
            }
        }

        private void txtTranslationY_TextChanged(object sender, EventArgs e)
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                float Y;
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];
                Single.TryParse(txtTranslationY.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out Y);

                pose.pose.t[1] = Y;
                SettingsChanged();
            }
        }

        private void txtTranslationZ_TextChanged(object sender, EventArgs e)
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                float Z;
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];
                Single.TryParse(txtTranslationZ.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out Z);

                pose.pose.t[2] = Z;
                SettingsChanged();
            }
        }

        private void txtId_TextChanged(object sender, EventArgs e)
        {
            if (lisMarkers.SelectedIndex >= 0)
            {
                int id;
                MarkerPose pose = oSettings.lMarkerPoses[lisMarkers.SelectedIndex];
                Int32.TryParse(txtId.Text, out id);

                pose.id = id;
                SettingsChanged();
            }
        }

        private void PlyFormat_CheckedChanged(object sender, EventArgs e)
        {
            if (rAsciiPly.Checked)
            {
                oSettings.bSaveAsBinaryPLY = false;
            }
            else
            {
                oSettings.bSaveAsBinaryPLY = true;
            }
        }

        private void cbCompressionLevel_SelectedIndexChanged(object sender, EventArgs e)
        {
            int index = cbCompressionLevel.SelectedIndex;
            if (index == 0)
                oSettings.iCompressionLevel = 0;
            else if (index == 2)
                oSettings.iCompressionLevel = 2;
            else
            {
                string value = cbCompressionLevel.SelectedItem.ToString();
                bool tryParse = Int32.TryParse(value, out oSettings.iCompressionLevel);
                if (!tryParse)
                    oSettings.iCompressionLevel = 0;
            }

            SettingsChanged();
        }


        private void chAutoExposureEnabled_CheckedChanged(object sender, EventArgs e)
        {
            oSettings.bAutoExposureEnabled = chAutoExposureEnabled.Checked;
            trManualExposure.Enabled = !chAutoExposureEnabled.Checked;
            SettingsChanged();
        }


        /// <summary>
        /// When the user scrolls on the trackbar, we wait a short amount of time to check if the user has scrolled again.
        /// This prevents the Manual Exposure to be set too often, and only sets it when the user has stopped scrolling.
        /// Code taken from: https://stackoverflow.com/a/15687418
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void trManualExposure_Scroll(object sender, EventArgs e)
        {
            if (scrollTimer == null)
            {
                // Will tick every 500ms
                scrollTimer = new Timer()
                {
                    Enabled = false,
                    Interval = 300,
                    Tag = (sender as TrackBar).Value
                };

                scrollTimer.Tick += (s, ea) =>
                {
                    // check to see if the value has changed since we last ticked
                    if (trManualExposure.Value == (int)scrollTimer.Tag)
                    {
                        // scrolling has stopped so we are good to go ahead and do stuff
                        scrollTimer.Stop();

                        // Send the changed exposure to the devices

                        //Clamp Exposure Step between -11 and 1
                        int exposureStep = trManualExposure.Value;
                        int exposureStepClamped = exposureStep < -11 ? -11 : exposureStep > 1 ? 1 : exposureStep;
                        oSettings.nExposureStep = exposureStepClamped;

                        SettingsChanged();

                        scrollTimer.Dispose();
                        scrollTimer = null;
                    }
                    else
                    {
                        // record the last value seen
                        scrollTimer.Tag = trManualExposure.Value;
                    }
                };
                scrollTimer.Start();
            }
        }

        private void SettingsForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            oServer.SetSettingsForm(null);
        }

        private void cbExtrinsicsFormat_SelectedIndexChanged(object sender, EventArgs e)
        {
            oSettings.eExtrinsicsFormat = (KinectSettings.ExtrinsicsStyle)cbExtrinsicsFormat.SelectedIndex;
            SettingsChanged();
        }

        private void rExportPointcloud_CheckedChanged(object sender, EventArgs e)
        {
            if (rExportPointcloud.Checked)
            {
                oSettings.eExportMode = KinectSettings.ExportMode.Pointcloud;
            }

            if (rExportRawFrames.Checked)
            {
                oSettings.eExportMode = KinectSettings.ExportMode.RawFrames;
            }

            SettingsChanged();
        }

        private void btApplyAllSettings_Click(object sender, EventArgs e)
        {
            UpdateClients();
        }

        private void chNetworkSync_CheckedChanged(object sender, EventArgs e)
        {
            oSettings.bNetworkSync = chNetworkSync.Checked;

            if (chNetworkSync.Checked)
                chHardwareSync.Enabled = false;

            else
                chHardwareSync.Enabled = true;

            SettingsChanged();
        }

        private void chHardwareSync_CheckedChanged(object sender, EventArgs e)
        {
            if (chHardwareSync.Checked)
            {
                chNetworkSync.Enabled = false;
            }

            else
            {
                chNetworkSync.Enabled = true;
            }

            SettingsChanged();

        }

        private void cbEnablePreview_CheckedChanged(object sender, EventArgs e)
        {
            oSettings.bPreviewEnabled = cbEnablePreview.Checked;
            SettingsChanged();
        }
    }
}