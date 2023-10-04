#define ENABLE_DISCOPANDA_DEBUGGING
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System;
using Newtonsoft.Json;
using UnityEngine;

namespace DiscoPanda
{
    public static class VideoUploader
    {
        const string sessionVersion = "V1"; 

        public static string _sessionId;
        public static string sessionId
        {
            get { return sessionVersion + _sessionId; }
            set { _sessionId = value; }
        }

        private static string apiUrlVideoUpload = "https://fox2fi7x68.execute-api.eu-west-1.amazonaws.com/prod/video-upload";

        public static async Task UploadVideoAsync(string videoPath, long startTime, long endTime, string apiKey)
        {
            Log($"UploadVideoAsync. {videoPath}");

            try
            {
                string presignedUploadUrl = await GetPresignedVideoUploadUrlAsync(startTime, endTime, apiKey);
                if (presignedUploadUrl == null)
                {
                    Debug.LogError("Error getting presigned URL.");
                    return;
                }

                Log($"presignedUploadUrl. {presignedUploadUrl}");

                string videoFilePath = $"{videoPath}";
                byte[] videoData;
                try
                {
                    videoData = File.ReadAllBytes(videoFilePath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error reading video file: {e.Message}");
                    return;
                }

                bool uploadSuccess = await UploadVideoDataAsync(presignedUploadUrl, videoData);
                if (uploadSuccess)
                {
                    Log("Video uploaded successfully.");
                }
                else
                {
                    Debug.LogError("Error uploading video.");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
#if !ENABLE_DISCOPANDA_DEBUGGING
                if (File.Exists(videoPath))
                    File.Delete(videoPath);

                string videoFolderPath = Path.GetDirectoryName(videoPath);
                if (Directory.Exists(videoFolderPath))
                    Directory.Delete(videoFolderPath);
#endif
            }
        }

        private static async Task<string> GetPresignedVideoUploadUrlAsync(long startTime, long endTime, string apiKey)
        {
            string url = $"{apiUrlVideoUpload}?api_key={apiKey}&session_id={sessionId}&start={startTime}&end={endTime}";
            using HttpClient httpClient = new HttpClient();
            HttpResponseMessage response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                Debug.LogError($"Response error: {responseString} url: {url}");
                return null;
            }

            string responseBody = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonConvert.DeserializeObject<PresignedUrlData>(responseBody);
            Log(responseBody);
            string presignedUploadUrl = jsonResponse.Url;

            return presignedUploadUrl;
        }

        private static async Task<bool> UploadVideoDataAsync(string presignedUploadUrl, byte[] videoData)
        {
            Log($"UploadVideoDataAsync. {presignedUploadUrl} {videoData.Length}");

            using HttpClient httpClient = new HttpClient();
            using ByteArrayContent content = new ByteArrayContent(videoData);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            HttpResponseMessage response = await httpClient.PutAsync(presignedUploadUrl, content);

            return response.IsSuccessStatusCode;
        }

        private class PresignedUrlData
        {
            public string Url { get; set; }
        }

        public static void Log(string message)
        {
#if ENABLE_DISCOPANDA_DEBUGGING
            UnityEngine.Debug.Log(message);
#endif
        }
    }
}