#include "pch.h"
#include "ImageToPointcloud.h"
#include "turbojpeg/turbojpeg.h"
#include "types.h"

namespace ImageProcessing
{
	static tjhandle turboJpeg;

	void InitImageProcessing()
	{
		turboJpeg = tjInitDecompress();
	}

	void CloseImageProcessing()
	{
		if (turboJpeg)
			tjDestroy(turboJpeg);
	}


	extern "C" IM2PC_API ImageSet * CreateImageSet()
	{
		ImageSet* newImageSet = new ImageSet();
		return newImageSet;
	}

	extern "C" IM2PC_API bool ChangeJPEGFromBuffer(ImageSet * imgsetPtr, int jpegWidth, int jpegHeight, char* jpegBuffer, int jpegSize)
	{
		if (jpegWidth < 1 || jpegHeight < 1 || jpegBuffer == NULL || jpegSize < 1)
			return NULL;

		imgsetPtr->colorImageWidth = jpegWidth;
		imgsetPtr->colorImageHeight = jpegHeight;
		imgsetPtr->jpegConvertedToBGRA = false;

		if (k4a_image_create_from_buffer(K4A_IMAGE_FORMAT_COLOR_MJPG, jpegWidth, jpegHeight, 0, reinterpret_cast<uint8_t*>(jpegBuffer), jpegSize, NULL, NULL, &imgsetPtr->colorImageJPEG) == K4A_RESULT_SUCCEEDED)
			return true;
		else
			return false;
	}

	extern "C" IM2PC_API bool ChangeBGRA32FromBuffer(ImageSet * imgsetPtr, int colorWidth, int colorHeight, char* colorBuffer)
	{
		if (colorWidth < 1 || colorHeight < 1 || colorBuffer == NULL)
			return NULL;

		imgsetPtr->colorImageWidth = colorWidth;
		imgsetPtr->colorImageHeight = colorHeight;

		if (k4a_image_create_from_buffer(K4A_IMAGE_FORMAT_COLOR_BGRA32, colorWidth, colorHeight, colorWidth * 4 * sizeof(uint8_t), reinterpret_cast<uint8_t*>(colorBuffer), colorWidth * colorHeight * 4 * sizeof(uint8_t), NULL, NULL, &imgsetPtr->colorImage) == K4A_RESULT_SUCCEEDED)
			return true;
		else
			return false;

	}

	extern "C" IM2PC_API bool ChangeDepthFromBuffer(ImageSet * imgsetPtr, int depthWidth, int depthHeight, char* depthBuffer)
	{
		if (depthWidth < 1 || depthHeight < 1 || depthBuffer == NULL)
			return NULL;

		imgsetPtr->depthImageWidth = depthWidth;
		imgsetPtr->depthImageHeight = depthHeight;
		imgsetPtr->depthConvertedToPointcloud = false;

		k4a_image_create_from_buffer(K4A_IMAGE_FORMAT_DEPTH16, depthWidth, depthHeight, 0, reinterpret_cast<uint8_t*>(depthBuffer), depthWidth * depthHeight * sizeof(int16_t), NULL, NULL, &imgsetPtr->depthImage);

	}

	extern "C" IM2PC_API bool ChangeCalibrationFromBuffer(ImageSet * imgsetPtr, char* calibrationBuffer, int calibrationSize, char colorRes, char depthMode)
	{
		if (calibrationBuffer == NULL || calibrationSize < 1)
			return NULL;

		k4a_calibration_get_from_raw(calibrationBuffer, calibrationSize, k4a_depth_mode_t(depthMode), k4a_color_resolution_t(colorRes), &imgsetPtr->calibration);
		imgsetPtr->transformation = k4a_transformation_create(&imgsetPtr->calibration);
	}


	extern "C" IM2PC_API bool CreatePointcloudFromImages(ImageSet * imgsetPtr)
	{
		if (!imgsetPtr->jpegConvertedToBGRA)
			if (!JPEG2BGRA32(imgsetPtr))
				return false;

		if (!MapDepthToColor(imgsetPtr))
			return false;

		if (!GeneratePointcloud(imgsetPtr))
			return false;

		if (!PointCloudImageToPoint3f(imgsetPtr))
			return false;

		return true;
	}

	/// <summary>
	/// Decompresses a raw MJPEG image to a BGRA cvMat using TurboJpeg
	/// </summary>
	extern "C" IM2PC_API bool JPEG2BGRA32(ImageSet * imgsetPtr)
	{
		if (imgsetPtr->colorImageJPEG == NULL)
			return false;
		if (k4a_image_get_buffer(imgsetPtr->colorImage) == NULL)
			return false;

		int jpegSize = k4a_image_get_size(imgsetPtr->colorImageJPEG);
		uint8_t* jpegBuffer = k4a_image_get_buffer(imgsetPtr->colorImageJPEG);

		if (imgsetPtr->colorImage == NULL || k4a_image_get_width_pixels(imgsetPtr->colorImage) != imgsetPtr->colorImageWidth || k4a_image_get_height_pixels(imgsetPtr->colorImage) != imgsetPtr->colorImageHeight)
			k4a_image_create(K4A_IMAGE_FORMAT_COLOR_BGRA32, imgsetPtr->colorImageWidth, imgsetPtr->colorImageHeight, imgsetPtr->colorImageWidth * 4 * sizeof(uint8_t), &imgsetPtr->colorImage);

		if (tjDecompress2(turboJpeg, jpegBuffer, jpegSize, k4a_image_get_buffer(imgsetPtr->colorImage), imgsetPtr->colorImageWidth, 0, imgsetPtr->colorImageHeight, TJPF_BGRA, TJFLAG_FASTDCT | TJFLAG_FASTUPSAMPLE))
			return false;
		else
		{
			imgsetPtr->jpegConvertedToBGRA = true;
			return true;
		}
	}

	extern "C" IM2PC_API bool ErodeDepthImageFilter(ImageSet * imgsetPtr, int filterKernelSize)
	{
		if (imgsetPtr->depthImage == NULL)
			return false;
		if (k4a_image_get_buffer(imgsetPtr->depthImage) == NULL)
			return false;

		uint8_t* depthBuffer = k4a_image_get_buffer(imgsetPtr->colorImageJPEG);

		cv::Mat cImgD = cv::Mat(imgsetPtr->depthImageHeight, imgsetPtr->depthImageWidth, CV_16UC1, depthBuffer);
		cv::Mat kernel = cv::getStructuringElement(cv::MORPH_RECT, cv::Size(filterKernelSize, filterKernelSize));
		int CLOSING = 3;
		cv::erode(cImgD, cImgD, kernel);
	}

	extern "C" IM2PC_API bool MapDepthToColor(ImageSet * imgsetPtr)
	{
		if (imgsetPtr->colorImage == NULL || imgsetPtr->depthImage == NULL)
			return false;
		if (k4a_image_get_buffer(imgsetPtr->colorImage) == NULL || k4a_image_get_buffer(imgsetPtr->depthImage) == NULL)
			return false;

		if (imgsetPtr->transformedDepthImage == NULL || k4a_image_get_width_pixels(imgsetPtr->transformedDepthImage) != imgsetPtr->colorImageWidth || k4a_image_get_height_pixels(imgsetPtr->transformedDepthImage) != imgsetPtr->colorImageHeight)
		{
			if (imgsetPtr->transformedDepthImage != NULL)
				k4a_image_release(imgsetPtr->transformedDepthImage);

			if (k4a_image_create(K4A_IMAGE_FORMAT_DEPTH16, imgsetPtr->colorImageWidth, imgsetPtr->colorImageHeight, imgsetPtr->colorImageWidth * (int)sizeof(uint16_t), &imgsetPtr->transformedDepthImage) == K4A_RESULT_FAILED)
				return false;
		}

		if (k4a_transformation_depth_image_to_color_camera(imgsetPtr->transformation, imgsetPtr->depthImage, imgsetPtr->transformedDepthImage) == K4A_RESULT_FAILED)
			return false;
		else
			return true;
	}

	/// <summary>
	/// Creates a Pointcloud out of the transformedDepthImage and saves it in PointcloudImage. Make sure to run MapDepthToColor before calling this function
	/// </summary>
	extern "C" IM2PC_API bool GeneratePointcloud(ImageSet * imgsetPtr)
	{
		if (imgsetPtr->colorImage == NULL || imgsetPtr->transformedDepthImage == NULL)
			return false;
		if (k4a_image_get_buffer(imgsetPtr->colorImage) == NULL || k4a_image_get_buffer(imgsetPtr->transformedDepthImage) == NULL)
			return false;

		if (imgsetPtr->pointcloud == NULL || k4a_image_get_width_pixels(imgsetPtr->pointcloud) != imgsetPtr->colorImageWidth || k4a_image_get_height_pixels(imgsetPtr->pointcloud) != imgsetPtr->colorImageHeight)
		{
			if (imgsetPtr->pointcloud != NULL)
				k4a_image_release(imgsetPtr->pointcloud);

			if (k4a_image_create(K4A_IMAGE_FORMAT_CUSTOM, imgsetPtr->colorImageWidth, imgsetPtr->colorImageHeight, imgsetPtr->colorImageWidth * 3 * (int)sizeof(int16_t), &imgsetPtr->pointcloud) == K4A_RESULT_FAILED)
				return false;
		}

		if (k4a_transformation_depth_image_to_point_cloud(imgsetPtr->transformation, imgsetPtr->transformedDepthImage, K4A_CALIBRATION_TYPE_COLOR, imgsetPtr->pointcloud))
			return false;
		else
			return true;
	}


	///<summary>
	///Translates the k4a_image_t pointcloud into a easier to handle Point3f array. Make sure to run MapDepthToColorFrame & GeneratePointcloud before calling this function
	///</summary>
	///<param name="pCameraSpacePoints"></param>
	extern "C" IM2PC_API bool PointCloudImageToPoint3f(ImageSet * imgsetPtr)
	{
		if (imgsetPtr->pointcloud == NULL)
			return false;

		if (k4a_image_get_buffer(imgsetPtr->pointcloud) == NULL)
			return false;

		if (imgsetPtr->pointcloud3f != NULL)
			delete[] imgsetPtr->pointcloud3f;

		long pointcloudSize = imgsetPtr->colorImageWidth * imgsetPtr->colorImageHeight;

		if (imgsetPtr->pointcloud3fSize != pointcloudSize || imgsetPtr->pointcloud3f == NULL)
		{
			if (imgsetPtr->pointcloud3f != NULL)
				delete[] imgsetPtr->pointcloud3f;

			imgsetPtr->pointcloud3f = new Point3f[pointcloudSize];
			imgsetPtr->pointcloud3fSize = pointcloudSize;
		}

		int16_t* pointCloudData = (int16_t*)k4a_image_get_buffer(imgsetPtr->pointcloud);

		for (int i = 0; i < imgsetPtr->colorImageHeight; i++)
		{
			for (int j = 0; j < imgsetPtr->colorImageWidth; j++)
			{
				imgsetPtr->pointcloud3f[j + i * imgsetPtr->colorImageWidth].X = pointCloudData[3 * (j + i * imgsetPtr->colorImageWidth) + 0];
				imgsetPtr->pointcloud3f[j + i * imgsetPtr->colorImageWidth].Y = pointCloudData[3 * (j + i * imgsetPtr->colorImageWidth) + 1];
				imgsetPtr->pointcloud3f[j + i * imgsetPtr->colorImageWidth].Z = pointCloudData[3 * (j + i * imgsetPtr->colorImageWidth) + 2];

				//We mark all vertices with a distance of 0 as invalid
				if (imgsetPtr->pointcloud3f[j + i * imgsetPtr->colorImageWidth].Z < 1)
					imgsetPtr->pointcloud3f[j + i * imgsetPtr->colorImageWidth].Invalid = true;
			}
		}
	}


	extern "C" IM2PC_API void Pointcloud3fTransformToWorld(ImageSet * imgsetPtr, float* transformationMatrix4x4)
	{
		//Scale Pointcloud to meters
		Matrix4x4 scaleToMeters = Matrix4x4(
			0.001f, 0.0f, 0.0f, 0.0f,
			0.0f, 0.001f, 0.0f, 0.0f,
			0.0f, 0.0f, 0.001f, 0.0f,
			0.0f, 0.0f, 0.0f, 1.0f);

		Matrix4x4 transformation = Matrix4x4(transformationMatrix4x4);
		Matrix4x4 toWorld = transformation * scaleToMeters;

		for (size_t i = 0; i < imgsetPtr->colorImageWidth * imgsetPtr->colorImageHeight; i++)
		{
			imgsetPtr->pointcloud3f[i] = toWorld * imgsetPtr->pointcloud3f[i];
		}
	}


	extern "C" IM2PC_API void Pointcloud3fMinify(ImageSet * imgsetPtr, float* boundingBox)
	{
		Point3f invalidPoint = Point3f(0, 0, 0, true);
		int goodVerticesCount = 0;

		Point3f* pointcloud = imgsetPtr->pointcloud3f;

		for (unsigned int vertexIndex = 0; vertexIndex < imgsetPtr->colorImageWidth * imgsetPtr->colorImageHeight; vertexIndex++)
		{
			Point3f* vertex = &pointcloud[vertexIndex];

			if (!vertex->Invalid)
			{
				if (vertex->X < boundingBox[0] || vertex->X > boundingBox[3]
					|| vertex->Y < boundingBox[1] || vertex->Y > boundingBox[4]
					|| vertex->Z < boundingBox[2] || vertex->Z > boundingBox[5])
				{
					vertex->Invalid = true;
					continue;
				}

				goodVerticesCount++;
			}
		}

		//The size of the pointcloud changes every frame, so we can dump the old buffers
		delete[] imgsetPtr->pointcloudMinified;
		delete[] imgsetPtr->colorMinified;

		if (goodVerticesCount > 0)
		{
			imgsetPtr->pointcloudMinified = new Point3s[goodVerticesCount];
			imgsetPtr->colorMinified = new RGBA[goodVerticesCount];
			imgsetPtr->minifiedPointcloudSize = goodVerticesCount;

			char* colorValues = reinterpret_cast<char*>(k4a_image_get_buffer(imgsetPtr->colorImage));

			//Copy all valid vertices into a clean vector
			int j = 0;
			for (unsigned int i = 0; i < imgsetPtr->colorImageWidth * imgsetPtr->colorImageHeight; i++)
			{
				if (!pointcloud[i].Invalid)
				{
					RGBA color;
					color.red = colorValues[i * 4];
					color.green = colorValues[(i * 4) + 1];
					color.blue = colorValues[(i * 4) + 2];

					imgsetPtr->pointcloudMinified[j] = pointcloud[i];
					imgsetPtr->colorMinified[j] = color;
					j++;
				}
			}
		}

		//If the pointcloud is empty, we can't have an array with zero elements
		else
		{
			imgsetPtr->pointcloudMinified = new Point3s[1];
			Point3s point(0, 0, 0);
			imgsetPtr->pointcloudMinified[0] = point;

			imgsetPtr->colorMinified = new RGBA[1];
			RGBA color;
			imgsetPtr->colorMinified[0] = color;

			imgsetPtr->minifiedPointcloudSize = 1;
		}
	}

	extern "C" IM2PC_API int GetColorImageWidth(ImageSet * imgsetPtr)
	{
		return imgsetPtr->colorImageWidth;
	}

	extern "C" IM2PC_API int GetColorImageHeight(ImageSet * imgsetPtr)
	{
		return imgsetPtr->colorImageHeight;
	}

	extern "C" IM2PC_API int GetColorImageSize(ImageSet * imgsetPtr)
	{
		return imgsetPtr->colorImageWidth * imgsetPtr->colorImageHeight * 4 * sizeof(int8_t);
	}

	extern "C" IM2PC_API int GetDepthImageWidth(ImageSet * imgsetPtr)
	{
		return imgsetPtr->colorImageWidth;
	}

	extern "C" IM2PC_API int GetDepthImageSize(ImageSet * imgsetPtr)
	{
		return imgsetPtr->colorImageHeight;

	}

	extern "C" IM2PC_API int GetPointcloud3fSize(ImageSet * imgsetPtr)
	{
		return imgsetPtr->pointcloud3fSize;
	}

	extern "C" IM2PC_API Point3f * GetPointCloudBuffer(ImageSet * imgsetPtr)
	{
		return imgsetPtr->pointcloud3f;
	}

	extern "C" IM2PC_API char* GetColorImageBuffer(ImageSet * imgsetPtr)
	{
		return reinterpret_cast<char*>(k4a_image_get_buffer(imgsetPtr->colorImage));
	}


	extern "C" IM2PC_API void DisposeImageSet(ImageSet * imgsetPtr)
	{
		if (imgsetPtr->colorImageJPEG != NULL)
		{
			k4a_image_release(imgsetPtr->colorImageJPEG);
			imgsetPtr->colorImageJPEG = NULL;
		}

		if (imgsetPtr->colorImage != NULL)
		{
			k4a_image_release(imgsetPtr->colorImage);
			imgsetPtr->colorImage = NULL;
		}

		if (imgsetPtr->depthImage != NULL)
		{
			k4a_image_release(imgsetPtr->depthImage);
			imgsetPtr->depthImage = NULL;
		}

		if (imgsetPtr->transformedDepthImage != NULL)
		{
			k4a_image_release(imgsetPtr->transformedDepthImage);
			imgsetPtr->transformedDepthImage = NULL;
		}

		if (imgsetPtr->pointcloud != NULL)
		{
			k4a_image_release(imgsetPtr->pointcloud);
			imgsetPtr->pointcloud = NULL;
		}

		delete[] imgsetPtr->pointcloud3f;
		k4a_transformation_destroy(imgsetPtr->transformation);

		delete[] imgsetPtr->pointcloudMinified;
		delete[] imgsetPtr->colorMinified;

		delete imgsetPtr;
		imgsetPtr = NULL;
	}
}





