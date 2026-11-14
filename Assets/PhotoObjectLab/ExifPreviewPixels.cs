using System;

// Coordinates use Unity's bottom-left pixel origin. Produces an upright,
// bounded-size preview so IMGUI never needs a rotated/clipped drawing matrix.
public static class ExifPreviewPixels
{
    public static T[] Prepare<T>(T[] source,int width,int height,int orientation,
                                int maxSize,out int outputWidth,out int outputHeight,
                                double focalX=0,double focalY=0,double principalX=0,double principalY=0,double radialK=0,
                                Func<T,T,T,T,double,double,T> interpolate=null)
    {
        if(source==null || width<=0 || height<=0 || maxSize<=0 || source.Length!=(long)width*height)
            throw new ArgumentException("Invalid preview pixel dimensions.");
        if(orientation<1 || orientation>8)orientation=1;
        bool swap=orientation>=5;
        int orientedWidth=swap?height:width,orientedHeight=swap?width:height;
        double scale=Math.Min(1.0,maxSize/(double)Math.Max(orientedWidth,orientedHeight));
        outputWidth=Math.Max(1,(int)Math.Round(orientedWidth*scale));
        outputHeight=Math.Max(1,(int)Math.Round(orientedHeight*scale));
        var output=new T[outputWidth*outputHeight];
        for(int y=0;y<outputHeight;y++)for(int x=0;x<outputWidth;x++)
        {
            // Interpolated previews preserve subpixel positions through rotation and lens correction.
            double dx=interpolate==null ? Math.Min(orientedWidth-1,(int)((x+.5)*orientedWidth/outputWidth))
                : Math.Max(0,Math.Min(orientedWidth-1,(x+.5)*orientedWidth/outputWidth-.5));
            double dy=interpolate==null ? Math.Min(orientedHeight-1,(int)((y+.5)*orientedHeight/outputHeight))
                : Math.Max(0,Math.Min(orientedHeight-1,(y+.5)*orientedHeight/outputHeight-.5));
            double sx=dx,sy=dy;
            switch(orientation)
            {
                case 2:sx=width-1-dx;break;
                case 3:sx=width-1-dx;sy=height-1-dy;break;
                case 4:sy=height-1-dy;break;
                case 5:sx=width-1-dy;sy=height-1-dx;break;
                case 6:sx=width-1-dy;sy=dx;break;
                case 7:sx=dy;sy=dx;break;
                case 8:sx=dy;sy=height-1-dx;break;
            }
            if(focalX>0 && focalY>0 && radialK!=0)
            {
                double nx=(sx-principalX)/focalX,ny=(height-1-sy-principalY)/focalY;
                double distortion=1+radialK*(nx*nx+ny*ny);
                sx=focalX*nx*distortion+principalX;
                sy=height-1-(focalY*ny*distortion+principalY);
                if(interpolate==null){sx=Math.Round(sx);sy=Math.Round(sy);}
            }
            if(sx>=0 && sx<width && sy>=0 && sy<height)
            {
                int x0=(int)sx,y0=(int)sy;
                if(interpolate==null)output[y*outputWidth+x]=source[y0*width+x0];
                else
                {
                    int x1=Math.Min(width-1,x0+1),y1=Math.Min(height-1,y0+1);
                    output[y*outputWidth+x]=interpolate(source[y0*width+x0],source[y0*width+x1],
                        source[y1*width+x0],source[y1*width+x1],sx-x0,sy-y0);
                }
            }
        }
        return output;
    }
}
