using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

// A pinhole view of the undistorted photo. The EXIF display rotation is applied
// to both camera roll and intrinsics. Geometry follows the point cloud frame.
public sealed class ColmapMatchedView : IDisposable
{
    Camera view;
    Transform frame;
    ColmapCameraDisplay.CameraRecord record;
    int orientation;
    Vector3 originalPosition;
    Quaternion originalRotation;
    Matrix4x4 originalProjection;
    Rect originalRect;
    float originalAspect, originalNear, originalFar, originalFov;
    bool originalOrthographic, originalPhysical;
    RenderTexture originalTarget, comparisonTarget;
    Camera comparisonCamera;
    int originalCullingMask;
    CameraClearFlags originalClearFlags;
    Color originalBackground;
    bool originalHDR, originalMSAA;
    Component pipelineCameraData;
    PropertyInfo postProcessProperty;
    bool originalPostProcess;
    Rect displayRect;
    public bool Active => view != null;
    public RenderTexture ComparisonTexture => comparisonTarget;
    public Camera ComparisonCamera => comparisonCamera;
    public Rect DisplayRect => displayRect;

    public bool TryEnter(Camera camera,Transform coordinateFrame,ColmapCameraDisplay.CameraRecord pose,
                         int photoOrientation,Rect availablePixels,out string error)
    {
        error=null;
        if(camera==null || coordinateFrame==null || pose==null || pose.imageWidth<=0 || pose.imageHeight<=0 || pose.focalLength<=0)
        {
            error="Camera calibration is missing. Regenerate the camera export.";
            return false;
        }
        Vector3 scale=coordinateFrame.lossyScale;
        if(scale.x<=0 || Mathf.Abs(scale.x-scale.y)>1e-4f || Mathf.Abs(scale.x-scale.z)>1e-4f)
        {
            error="Matched views require a positive uniform scale on the bottle's coordinate frame.";
            return false;
        }
        if(pose.corners==null || pose.corners.Length!=4 ||
            (pose.forwardTip-pose.center).sqrMagnitude<1e-12f ||
            (pose.upTip-(pose.corners[0]+pose.corners[1])*.5f).sqrMagnitude<1e-12f)
        {
            error="The selected camera orientation is invalid.";
            return false;
        }
        if(!Active)
        {
            view=camera;
            originalPosition=view.transform.position;originalRotation=view.transform.rotation;
            originalRect=view.rect;originalAspect=view.aspect;originalProjection=view.projectionMatrix;
            originalNear=view.nearClipPlane;originalFar=view.farClipPlane;originalFov=view.fieldOfView;
            originalOrthographic=view.orthographic;originalPhysical=view.usePhysicalProperties;
            originalTarget=view.targetTexture;originalClearFlags=view.clearFlags;originalBackground=view.backgroundColor;
            originalHDR=view.allowHDR;originalMSAA=view.allowMSAA;
            originalCullingMask=view.cullingMask;
            // URP permits target alpha when post-processing is disabled. Resolve
            // its optional camera component without a hard pipeline dependency.
            pipelineCameraData=view.GetComponent("UniversalAdditionalCameraData");
            postProcessProperty=pipelineCameraData!=null?pipelineCameraData.GetType().GetProperty("renderPostProcessing"):null;
            if(postProcessProperty!=null && postProcessProperty.PropertyType==typeof(bool) && postProcessProperty.CanWrite)
            {
                originalPostProcess=(bool)postProcessProperty.GetValue(pipelineCameraData);
                postProcessProperty.SetValue(pipelineCameraData,false);
            }
        }
        frame=coordinateFrame;record=pose;orientation=photoOrientation>=1&&photoOrientation<=8?photoOrientation:1;
        try { Refresh(availablePixels);return true; }
        catch(Exception exception){error="Comparison view: "+exception.Message;Dispose();return false;}
    }

    public void Refresh(Rect availablePixels)
    {
        if(!Active || frame==null)return;
        Vector3 forward=frame.TransformVector(record.forwardTip-record.center).normalized;
        Vector3 up=frame.TransformVector(record.upTip-(record.corners[0]+record.corners[1])*.5f).normalized;
        float roll=orientation==3?180:orientation>=5&&orientation<=7?90:orientation==8?-90:0;
        Quaternion rotation=Quaternion.LookRotation(forward,up)*Quaternion.AngleAxis(roll,Vector3.forward);
        view.transform.SetPositionAndRotation(frame.TransformPoint(record.center),rotation);
        GetIntrinsics(record,orientation,out float width,out float height,out float fx,out float fy,out float cx,out float cy);
        view.usePhysicalProperties=false;view.orthographic=false;
        view.nearClipPlane=Mathf.Max(.001f,.01f*frame.lossyScale.x);
        view.farClipPlane=Mathf.Max(originalFar,view.nearClipPlane*10000);
        view.fieldOfView=2*Mathf.Atan(height/(2*record.focalLength))*Mathf.Rad2Deg;
        view.aspect=width/height;
        displayRect=Fit(availablePixels,view.aspect);
        EnsureComparisonTarget(Mathf.Max(1,Mathf.RoundToInt(displayRect.width)),Mathf.Max(1,Mathf.RoundToInt(displayRect.height)));
        view.targetTexture=originalTarget;
        view.pixelRect=displayRect;
        // Keep a screen camera active for Unity's Game view and IMGUI. A
        // separate aligned camera renders the model into an alpha-bearing image.
        view.cullingMask=0;
        view.clearFlags=CameraClearFlags.SolidColor;
        view.backgroundColor=Color.clear;
        view.allowHDR=false;view.allowMSAA=false;
        view.projectionMatrix=Projection(width,height,fx,fy,cx,cy,view.nearClipPlane,view.farClipPlane);
        if(comparisonCamera==null)
        {
            var cameraObject=new GameObject("Matched comparison renderer");
            cameraObject.hideFlags=HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(view.transform,false);
            comparisonCamera=cameraObject.AddComponent<Camera>();
        }
        comparisonCamera.CopyFrom(view);
        comparisonCamera.targetTexture=comparisonTarget;
        comparisonCamera.rect=new Rect(0,0,1,1);
        comparisonCamera.aspect=width/height;
        comparisonCamera.cullingMask=originalCullingMask;
        comparisonCamera.depth=view.depth-1;
        comparisonCamera.projectionMatrix=view.projectionMatrix;
        comparisonCamera.enabled=true;
    }

    static void GetIntrinsics(ColmapCameraDisplay.CameraRecord p,int o,out float w,out float h,
                              out float fx,out float fy,out float cx,out float cy)
    {
        w=p.imageWidth;h=p.imageHeight;fx=p.focalLength;fy=p.focalLength;cx=p.principalX;cy=p.principalY;
        switch(o)
        {
            case 2:fx=-fx;cx=w-cx;break;
            case 3:cx=w-cx;cy=h-cy;break;
            case 4:fy=-fy;cy=h-cy;break;
            case 5:w=p.imageHeight;h=p.imageWidth;fx=-fx;cx=p.principalY;cy=p.principalX;break;
            case 6:w=p.imageHeight;h=p.imageWidth;cx=p.imageHeight-p.principalY;cy=p.principalX;break;
            case 7:w=p.imageHeight;h=p.imageWidth;fy=-fy;cx=p.imageHeight-p.principalY;cy=p.imageWidth-p.principalX;break;
            case 8:w=p.imageHeight;h=p.imageWidth;cx=p.principalY;cy=p.imageWidth-p.principalX;break;
        }
    }

    public static Matrix4x4 Projection(float w,float h,float fx,float fy,float cx,float cy,float near,float far)
    {
        Matrix4x4 p=new Matrix4x4();
        p.m00=2*fx/w;p.m11=2*fy/h;p.m02=1-2*cx/w;p.m12=2*cy/h-1;
        p.m22=-(far+near)/(far-near);p.m23=-2*far*near/(far-near);p.m32=-1;
        return p;
    }

    public static Rect Fit(Rect area,float aspect)
    {
        if(area.width<=0 || area.height<=0)area=new Rect(0,0,Mathf.Max(Screen.width,1),Mathf.Max(Screen.height,1));
        float w=Mathf.Min(area.width,area.height*aspect),h=w/aspect;
        return new Rect(area.center.x-w*.5f,area.center.y-h*.5f,w,h);
    }

    public void DrawMargins()
    {
        if(!Active)return;
        Color saved=GUI.color;GUI.color=Color.black;
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);
        GUI.color=saved;
    }

    public void DrawComparison(Texture photo,bool showPhoto,float photoOpacity,float reconstructionOpacity,bool tintCyan=false)
    {
        if(!Active)return;
        Rect area=new Rect(displayRect.x,Screen.height-displayRect.yMax,displayRect.width,displayRect.height);
        Color saved=GUI.color;
        try
        {
            if(showPhoto && photo!=null)
            {
                GUI.color=new Color(1,1,1,Mathf.Clamp01(photoOpacity));
                GUI.DrawTexture(area,photo,ScaleMode.StretchToFill,true);
            }
            if(comparisonTarget!=null)
            {
                GUI.color=tintCyan?new Color(.12f,1,1,Mathf.Clamp01(reconstructionOpacity)):
                    new Color(1,1,1,Mathf.Clamp01(reconstructionOpacity));
                GUI.DrawTexture(area,comparisonTarget,ScaleMode.StretchToFill,true);
            }
        }
        finally{GUI.color=saved;}
    }

    void EnsureComparisonTarget(int width,int height)
    {
        if(comparisonTarget!=null && comparisonTarget.width==width && comparisonTarget.height==height)return;
        if(comparisonCamera!=null)comparisonCamera.targetTexture=null;
        ReleaseTarget();
        comparisonTarget=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32)
        {
            name="Matched reconstruction with transparent background",
            antiAliasing=1,useMipMap=false,autoGenerateMips=false,filterMode=FilterMode.Bilinear
        };
        if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null && !comparisonTarget.Create())
            throw new InvalidOperationException("Could not allocate the reconstruction comparison image.");
    }

    void ReleaseTarget()
    {
        if(comparisonTarget==null)return;
        comparisonTarget.Release();
        if(Application.isPlaying)UnityEngine.Object.Destroy(comparisonTarget);
        else UnityEngine.Object.DestroyImmediate(comparisonTarget);
        comparisonTarget=null;
    }

    public void Dispose()
    {
        if(view!=null)
        {
            view.targetTexture=originalTarget;
            view.cullingMask=originalCullingMask;
            view.clearFlags=originalClearFlags;view.backgroundColor=originalBackground;
            view.allowHDR=originalHDR;view.allowMSAA=originalMSAA;
            if(pipelineCameraData!=null && postProcessProperty!=null && postProcessProperty.CanWrite)
                postProcessProperty.SetValue(pipelineCameraData,originalPostProcess);
            view.transform.SetPositionAndRotation(originalPosition,originalRotation);
            view.rect=originalRect;view.nearClipPlane=originalNear;view.farClipPlane=originalFar;
            view.usePhysicalProperties=originalPhysical;view.orthographic=originalOrthographic;
            view.fieldOfView=originalFov;view.aspect=originalAspect;view.projectionMatrix=originalProjection;
        }
        if(comparisonCamera!=null)
        {
            comparisonCamera.enabled=false;comparisonCamera.targetTexture=null;
            if(Application.isPlaying)UnityEngine.Object.Destroy(comparisonCamera.gameObject);
            else UnityEngine.Object.DestroyImmediate(comparisonCamera.gameObject);
            comparisonCamera=null;
        }
        ReleaseTarget();pipelineCameraData=null;postProcessProperty=null;
        view=null;frame=null;record=null;
    }
}
