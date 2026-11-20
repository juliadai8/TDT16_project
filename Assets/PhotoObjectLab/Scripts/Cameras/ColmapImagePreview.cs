using System;
using System.IO;
using UnityEngine;

public sealed class ColmapImagePreview : IDisposable
{
    public Texture2D Texture { get; private set; }
    public string Status { get; private set; }
    public int Orientation { get; private set; } = 1;
    public bool IsUndistorted { get; private set; }
    bool ownsTexture;
    int imageId = -1;
    string imageName;

    public void SetImage(ColmapCameraDisplay.CameraRecord record,bool undistort=false)
    {
        if (record != null && record.imageId == imageId && record.imageName == imageName && undistort==IsUndistorted) return;
        Dispose();
        if (record == null) return;
        imageId = record.imageId;
        imageName = record.imageName;
        string filename = Path.GetFileName(record.imageName.Replace('\\','/'));
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
        {
            Status = "Preview supports JPG and PNG images.";
            return;
        }
        string project = Path.GetDirectoryName(Application.dataPath);
        string[] folders = {
            "Reconstruction/bottle/images", "Reconstruction/bottle/dense/images",
            "Reconstruction/bottle/openmvs/images", "Reconstruction/bottle/openmvs/undistorted_images",
            "Assets/PhotoObjectLab/Photos", "Assets/PhotoObjectLab/Resources/BottlePhotos"
        };
        foreach (string folder in folders)
        {
            string path = Path.Combine(project, folder, filename);
            if (!File.Exists(path)) continue;
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2,2,TextureFormat.RGB24,false);
                byte[] bytes = File.ReadAllBytes(path);
                int orientation = JpegExifOrientation.Read(bytes);
                if (!ImageConversion.LoadImage(texture, bytes, false))
                    throw new InvalidDataException("Image could not be decoded.");
                Texture2D preview = PreparePreview(texture,orientation,undistort?record:null);
                Release(texture);
                texture = preview;
                Texture = texture;
                Orientation = orientation;
                IsUndistorted = undistort;
                ownsTexture = true;
                return;
            }
            catch (Exception)
            {
                Release(texture);
                Status = "The matching image was found but could not be loaded.";
                return;
            }
        }
        Texture = Resources.Load<Texture2D>("BottlePhotos/" + Path.GetFileNameWithoutExtension(filename));
        if(Texture!=null && undistort)
            Status="This imported preview has no original calibration metadata; use the original photo for a calibrated comparison.";
        if (Texture == null)
            Status = "Original photo not available locally.\nAdd the matching photo to Reconstruction/bottle/images to preview it.";
    }

    public void Draw(Rect area)
    {
        if (Texture == null) return;
        GUI.DrawTexture(area,Texture,ScaleMode.ScaleToFit);
    }

    static Texture2D PreparePreview(Texture2D original,int orientation,ColmapCameraDisplay.CameraRecord calibration)
    {
        double fx=0,fy=0,cx=0,cy=0,k=0;
        if(calibration!=null && calibration.imageWidth>0 && calibration.imageHeight>0)
        {
            fx=calibration.focalLength*original.width/(double)calibration.imageWidth;
            fy=calibration.focalLength*original.height/(double)calibration.imageHeight;
            cx=calibration.principalX*original.width/(double)calibration.imageWidth;
            cy=calibration.principalY*original.height/(double)calibration.imageHeight;
            k=calibration.radialK;
        }
        Color32[] pixels = ExifPreviewPixels.Prepare(original.GetPixels32(),original.width,original.height,
            orientation,calibration!=null ? 2560 : 1024,out int width,out int height,fx,fy,cx,cy,k,
            (a,b,c,d,x,y) => Color32.Lerp(Color32.Lerp(a,b,(float)x),Color32.Lerp(c,d,(float)x),(float)y));
        var preview = new Texture2D(width,height,TextureFormat.RGBA32,false);
        try
        {
            preview.name = "Upright camera image preview";
            preview.filterMode = FilterMode.Bilinear;
            preview.wrapMode = TextureWrapMode.Clamp;
            preview.SetPixels32(pixels);
            preview.Apply(false,true);
            return preview;
        }
        catch { Release(preview); throw; }
    }

    public void Dispose()
    {
        if (ownsTexture) Release(Texture);
        Texture=null; ownsTexture=false; imageId=-1; imageName=null; Status=null; Orientation=1; IsUndistorted=false;
    }
    static void Release(UnityEngine.Object value)
    {
        if(value==null)return;
        if(Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
    }
}
