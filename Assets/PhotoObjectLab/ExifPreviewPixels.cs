using System;

// Coordinates use Unity's bottom-left pixel origin. Produces an upright,
// bounded-size preview so IMGUI never needs a rotated/clipped drawing matrix.
public static class ExifPreviewPixels
{
    public static T[] Prepare<T>(T[] source,int width,int height,int orientation,
                                int maxSize,out int outputWidth,out int outputHeight,
                                double focalX=0,double focalY=0,double principalX=0,double principalY=0,double radialK=0)
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
            int dx=Math.Min(orientedWidth-1,(int)((x+.5)*orientedWidth/outputWidth));
            int dy=Math.Min(orientedHeight-1,(int)((y+.5)*orientedHeight/outputHeight));
            int sx=dx,sy=dy;
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
                sx=(int)Math.Round(focalX*nx*distortion+principalX);
                sy=height-1-(int)Math.Round(focalY*ny*distortion+principalY);
            }
            if(sx>=0 && sx<width && sy>=0 && sy<height)
                output[y*outputWidth+x]=source[sy*width+sx];
        }
        return output;
    }
}
