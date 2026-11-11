using System;

// Reads the TIFF orientation tag from JPEG EXIF without decoding the image.
// Invalid or absent metadata falls back to the unrotated display orientation.
public static class JpegExifOrientation
{
    public static int Read(byte[] data)
    {
        if (data == null || data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return 1;
        int position = 2;
        while (position < data.Length)
        {
            if (data[position++] != 0xFF) break;
            while (position < data.Length && data[position] == 0xFF) position++;
            if (position >= data.Length) break;
            int marker = data[position++];
            if (marker == 0xDA || marker == 0xD9) break;
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
            if (position > data.Length-2) break;
            int length = (data[position] << 8) | data[position+1];
            if (length < 2 || length > data.Length-position) break;
            int start = position+2, end = position+length;
            if (marker == 0xE1 && start <= end-14 && data[start] == 'E' && data[start+1] == 'x' &&
                data[start+2] == 'i' && data[start+3] == 'f' && data[start+4] == 0 && data[start+5] == 0)
            {
                int tiff = start+6;
                bool little = data[tiff] == 'I' && data[tiff+1] == 'I';
                bool big = data[tiff] == 'M' && data[tiff+1] == 'M';
                if ((!little && !big) || U16(data,tiff+2,little) != 42) return 1;
                uint offset = U32(data,tiff+4,little);
                if (offset < 8 || offset > (uint)(end-tiff-2)) return 1;
                int directory = tiff+(int)offset;
                int entries = U16(data,directory,little);
                if (entries > (end-directory-2)/12) return 1;
                for (int i=0;i<entries;i++)
                {
                    int entry=directory+2+i*12;
                    if (U16(data,entry,little) != 0x0112) continue;
                    if (U16(data,entry+2,little) != 3 || U32(data,entry+4,little) != 1) return 1;
                    int value=U16(data,entry+8,little);
                    return value >= 1 && value <= 8 ? value : 1;
                }
            }
            position = end;
        }
        return 1;
    }

    static int U16(byte[] data,int at,bool little) => little
        ? data[at] | (data[at+1]<<8) : (data[at]<<8) | data[at+1];
    static uint U32(byte[] data,int at,bool little) => little
        ? (uint)(data[at] | data[at+1]<<8 | data[at+2]<<16 | data[at+3]<<24)
        : (uint)(data[at]<<24 | data[at+1]<<16 | data[at+2]<<8 | data[at+3]);
}
