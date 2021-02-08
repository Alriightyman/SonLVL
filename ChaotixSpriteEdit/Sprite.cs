using SonicRetro.SonLVL.API;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace ChaotixSpriteEdit
{
	[Serializable]
	public struct Sprite
	{
		public Point Offset;
		public BitmapBits Image;
		public int X { get { return Offset.X; } set { Offset.X = value; } }
		public int Y { get { return Offset.Y; } set { Offset.Y = value; } }
		public int Width => Image.Width;
		public int Height => Image.Height;
		public Size Size => Image.Size;
		public int Left => X;
		public int Top => Y;
		public int Right => X + Width;
		public int Bottom => Y + Height;
		public Rectangle Bounds => new Rectangle(Offset, Size);

		public Sprite(BitmapBits spr, Point off)
		{
			Image = spr;
			Offset = off;
		}

		public Sprite(Sprite sprite)
		{
			Image = new BitmapBits(sprite.Image);
			Offset = sprite.Offset;
		}

		public Sprite(params Sprite[] sprites)
			: this((IEnumerable<Sprite>)sprites)
		{
		}

		public Sprite(IEnumerable<Sprite> sprites)
		{
			List<Sprite> sprlst = new List<Sprite>(sprites);
			int left = 0;
			int right = 0;
			int top = 0;
			int bottom = 0;
			bool first = true;
			foreach (Sprite spr in sprlst)
				if (spr.Image != null)
					if (first)
					{
						left = spr.Left;
						right = spr.Right;
						top = spr.Top;
						bottom = spr.Bottom;
						first = false;
					}
					else
					{
						left = Math.Min(spr.Left, left);
						right = Math.Max(spr.Right, right);
						top = Math.Min(spr.Top, top);
						bottom = Math.Max(spr.Bottom, bottom);
					}
			Offset = new Point(left, top);
			Image = new BitmapBits(right - left, bottom - top);
			for (int i = 0; i < sprlst.Count; i++)
				if (sprlst[i].Image != null)
				{
					bool comp = false;
					for (int j = 0; j < i; j++)
						if (sprlst[j].Image != null && sprlst[i].Bounds.IntersectsWith(sprlst[j].Bounds))
						{
							comp = true;
							break;
						}
					if (comp)
						Image.DrawBitmapComposited(sprlst[i].Image, new Point(sprlst[i].X - left, sprlst[i].Y - top));
					else
						Image.DrawBitmap(sprlst[i].Image, new Point(sprlst[i].X - left, sprlst[i].Y - top));
				}
		}

		private Rectangle GetUsedRange()
		{
			int x;
			int y;
			Rectangle result = new Rectangle();

			// get first used pixel at left side
			for (x = 0; x < Width; x++)
			{
				for (y = 0; y < Height; y++)
				{
					if (Image[x, y] > 0)
						break;
				}
				if (y < Height)
					break;
			}
			result.X = x;

			// get first used pixel at right side
			for (x = Width - 1; x >= result.X; x--)
			{
				for (y = 0; y < Height; y++)
				{
					if (Image[x, y] > 0)
						break;
				}
				if (y < Height)
					break;
			}
			result.Width = x + 1 - result.X;

			// get first used pixel at top side
			for (y = 0; y < Height; y++)
			{
				for (x = 0; x < Width; x++)
				{
					if (Image[x, y] > 0)
						break;
				}
				if (x < Width)
					break;
			}
			result.Y = y;

			// get first used pixel at bottom side
			for (y = Height - 1; y >= result.Y; y--)
			{
				for (x = 0; x < Width; x++)
				{
					if (Image[x, y] > 0)
						break;
				}
				if (x < Width)
					break;
			}
			result.Height = y + 1 - result.Y;
			return result;
		}

		public Sprite Crop(Rectangle rect)
		{
			if (rect.Width == Width && rect.Height == Height)
				return this;
			if (rect.Width == 0 && rect.Height == 0)
				return new Sprite();    // return empty sprite

			BitmapBits newimg = new BitmapBits(rect.Width, rect.Height);
			newimg.DrawBitmapBounded(Image, -rect.X, -rect.Y);
			return new Sprite(newimg, new Point(X + rect.X, Y + rect.Y));
		}

		public Sprite Trim()
		{
			Rectangle used = GetUsedRange();
			if (used.Width == 0 || used.Height == 0)
				return this;    // don't trim fully transparent images (else we won't be able to select them)
			else
				return Crop(used);
		}

		public void Flip(bool xflip, bool yflip)
		{
			Image.Flip(xflip, yflip);
			if (xflip) X = -(Width + X);
			if (yflip) Y = -(Height + Y);
		}

		public static Sprite LoadChaotixSprite(string filename, bool isPacked = false) 
		{
			byte[] data;

			if(isPacked)
			{
				data = UnpackChaotixArt(File.ReadAllBytes(filename), 0);
			}
			else
			{
				data = File.ReadAllBytes(filename);
			}

			return LoadChaotixSprite(data, 0); 
		}

		public static Sprite LoadChaotixSprite(byte[] file, int addr)
		{
			short left = ByteConverter.ToInt16(file, addr);
			addr += 2;
			short right = ByteConverter.ToInt16(file, addr);
			addr += 2;
			sbyte top = (sbyte)file[addr];
			addr += 2;
			sbyte bottom = (sbyte)file[addr];
			addr += 2;
			BitmapBits bmp = new BitmapBits(right - left + 1, bottom - top + 1);
			sbyte y;
			while (true)
			{
				sbyte xl = (sbyte)file[addr++];
				sbyte xr = (sbyte)file[addr++];
				if (xl == xr) 
					break;
				y = (sbyte)file[addr];
				addr += 2;
				Array.Copy(file, addr, bmp.Bits, bmp.GetPixelIndex(xl - left, y - top), xr - xl);
				addr += xr - xl;
			}
			return new Sprite(bmp, new Point(left, top));
		}

		public void SaveChaotixSprite(string filename, bool isPacked = false)
		{
			byte[] result;

			if (isPacked)
			{
				result = ConvertToPacked();
			}
			else
			{
				result = ConvertToUnpacked();
			}

			File.WriteAllBytes(filename, result);
		}

		#region Chaotix Art Compression

		static byte frb, sb;
		// data is packed data
		#region internal classes for Conversion to Packed format
		internal interface IData
		{
			long Data { get; }
		}

		internal class Dx : IData
		{
			private long dat;
			public Dx(long d)
			{
				dat = d;
			}

			public long Data => dat;
		}

		internal class Dy : IData
		{
			private long dat;
			public Dy(long d)
			{
				dat = d;
			}

			public long Data => dat;
		}

		internal class Dc : IData
		{
			private long dat;
			public Dc(long d)
			{
				dat = d;
			}

			public long Data => dat;
		}

		#endregion

		private byte[] ConvertToUnpacked()
		{
			List<byte> result = new List<byte>();
			int left = Left & ~1;
			int right = (Right & 1) == 1 ? Right + 1 : Right;

			// header
			// initial X
			result.AddRange(ByteConverter.GetBytes((short)left));
			// compression marker (0)/ Final X
			result.AddRange(ByteConverter.GetBytes((short)(right - 1)));
			// Initial Y
			result.Add((byte)(sbyte)Top);
			result.Add((byte)0);
			// Final Y
			result.Add((byte)(sbyte)(Bottom - 1));
			result.Add((byte)0);

			for (int y = 0; y < Height; y++)
			{
				int xl = -1;
				for (int x = 0; x < Width; x++)
				{
					var value = Image[x, y];
					if (value != 0)
					{
						xl = x;
						break;
					}
				}

				if (xl == -1)
				{
					continue;
				}

				int xr = 0;
				for (int x = Width - 1; x >= xl; x--)
				{
					var value = Image[x, y];
					if (value != 0)
					{
						xr = x + 1;
						break;
					}
				}

				xl &= ~1;
				if ((xr - xl) % 2 != 0)
				{
					++xr;
				}

				result.Add((byte)(sbyte)(xl + left));
				result.Add((byte)(sbyte)(xr + left));
				result.Add((byte)(sbyte)(y + Top));
				result.Add((byte)0);

				for (int x = xl; x < xr; x++)
				{
					result.Add((byte)(x < Width ? Image[x, y] : 0));
				}
			}

			result.AddRange(ByteConverter.GetBytes((short)0));

			if (result.Count % 4 != 0)
			{
				result.AddRange(new byte[4 - (result.Count % 4)]);
			}

			return result.ToArray();
		}

		private byte[] ConvertToPacked()
		{
			byte[] result;
		
			result = PackChaotixArt(this.ConvertToUnpacked());

			return result;
		}

		public static byte[] UnpackChaotixArt(byte[] data, int index)
		{
			List<byte> result = new List<byte>();

			// reset these
			frb = sb = 0;

			// $00-$01: size (skip)
			index += 2;

			byte compressionMarker = data[index++];

			if(compressionMarker != 0x42)
			{
				System.Windows.Forms.MessageBox.Show($"Error: Compression Marker should be 0x42 but found {compressionMarker}",
													"Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
				return null;
			}

			// $03: Initial X, Left side
			byte left = data[index++];
			// $4 - Bitcount for X.
			byte bitCountX = data[index++];
			// $5 - Initial Y/Base Y/Top Most possible Y from Center Point.
			byte top = data[index++];
			// $6 - Bitcount for Y.
			byte bitCountY = data[index++];
			// $7 - Base Palette Color Index [Usually $00 unless you are not setting it in the ArtQueue Load]
			byte paletteColorIndex = data[index++];
			// $8 - Bitcount for Palette Color Entries.
			byte paletteBitcount = data[index++];

			// Final X Pixel Location[Sets Initial X Prior to calling BitStream]
			byte w = ReadBitStream(bitCountX, data, ref index);

			// Final Y Pixel Location [Sets Initial Y Prior to calling BitStream]
			byte h = ReadBitStream(bitCountY, data, ref index);

			{ // not raw
				// $00-$01: Initial X
				result.AddRange(ByteConverter.GetBytes(SignedExtendByteToWord(left)));

				// $02: Compression Marker - always $00 for unpacked
				result.Add(0);

				// $03: Final X
				result.Add((byte)(left + w));

				// $04-05: Initial Y[First byte is relevant second byte is usually always $00, example: $EE00]
				result.AddRange(ByteConverter.GetBytes((short)(top << 8)));

				// $6-7 - Final Y. [First byte relevant, see Initial Y for example.]
				result.AddRange(ByteConverter.GetBytes((short)((top + h) << 8)));

				while(true)
				{
					// get row data start and end
					byte xst = ReadBitStream(bitCountX,data, ref index), xnd = ReadBitStream(bitCountX, data, ref index);

					if (result.Count > 0x139)
					{
						int x = 0;
					}

					// Byte - Row Starting X within the Initial/Final X boundary.
					result.Add((byte)(xst + left));
					// Byte - Row End X within the Initial/Final X Boundary.
					result.Add((byte)(xnd + left));

					// if start and end are same, end
					if (xst == xnd)
					{
						break;
					}

					// get y-offset of the row
					byte y = ReadBitStream(bitCountY, data, ref index);

					// write row header
					{ // NOT raw
						// Word - Current Y Row [Starts from the Initial Y, Same First byte relevant as above.]
						result.AddRange(ByteConverter.GetBytes((short)((y + top) << 8)));
					}

					// calculate pixels in the line
					int n = xnd - xst;

					// check if xst is less than xnd
					if (n < 0)
					{
						//e("Unable to convert at " + sin.Position + "; row start '" + xst + "' is more than row end '" + xnd + "'!");
						//return;
					}

					// loop for all pixels on that row
					for (; n != 0; n--)
					{
						// Byte - Pixels to draw, Always has to end on an even number of pixels. etc.
						result.Add((byte)(ReadBitStream(paletteBitcount, data, ref index) + paletteColorIndex));
					}
				}
			}

			return result.ToArray();
		}

		public static byte[] PackChaotixArt(byte[] unpackedData)
		{
			frb = 0;
			int index = 0;
			
			List<byte> result = new List<byte>();

			List<IData> data = new List<IData>();
			byte rx, ry, w, h;
			short sz = 6;

			{
				// $0-1 - Initial X
				rx = (byte)ByteConverter.ToInt16(unpackedData, index);
				index += 2;

				byte compressionMarker = unpackedData[index++];
				if (compressionMarker != 0)
				{
					//l("Warn: Compression marker not set to 0x00! Are you sure this is unpacked format data file?");
				}

				// $3 - Final X which gets sign extended into a word later.
				w = (byte)(unpackedData[index++] - rx);
				data.Add(new Dx(w));
				//result.Add(w);

				// $4-5 - Initial Y[First byte is relevant second byte is usually always $00, example: $EE00]
				ry = (byte)(ByteConverter.ToInt16(unpackedData, index) >> 8);
				index += 2;

				// $6-7 - Final Y. [First byte relevant, see Initial Y for example.]
				h = (byte)((byte)(ByteConverter.ToInt16(unpackedData, index) >> 8) - ry);
				index += 2;

				data.Add(new Dy(h));
				//result.Add(h);
			}

			// loop until end token
			while (true)
			{
				sz += 4;
				byte xst = (byte)(unpackedData[index++] - rx), xnd = (byte)(unpackedData[index++] - rx);

				// Byte - Row Starting X within the Initial/Final X boundary.
				data.Add(new Dx(xst));
				//result.Add(xst);

				// Byte - Row End X within the Initial/Final X Boundary.
				data.Add(new Dx(xnd));
				//result.Add(xnd);

				if (xst == xnd)
				{
					break;
				}

				{
					// Word - Current Y Row [Starts from the Initial Y, Same First byte relevant as above.]
					//a.Add(new Dy((byte)(rw() >> 8) - ry));
					data.Add(new Dy((byte)((ByteConverter.ToInt16(unpackedData, index) >> 8) - ry))); // TODO: check
					//result.Add((byte)((ByteConverter.ToInt16(unpackedData, index) >> 8) - ry));  // TODO: check
					index += 2;
				}

				// calculate pixels in the line
				int n = xnd - xst;

				// check if xst is less than xnd
				if (n < 0)
				{
					//e("Unable to read at " + sin.Position + "; row start '" + xst + "' is more than row end '" + xnd + "'!");
					//return;
				}

				// loop for all pixels on that row
				for (; n != 0; n--)
				{
					// Byte - Pixels to draw, Always has to end on an even number of pixels. etc.
					//a.Add(new Dc(rb()));
					//result.Add(unpackedData[index++]);
					data.Add(new Dc(unpackedData[index++]));
					sz++;
				}
			}

			// gather bitcounts
			long mx = 0, my = 0, mc = 0;

			foreach (IData d in data)
			{
				if (d is Dx)
				{
					if (d.Data > mx)
					{
						mx = d.Data;
					}
				}
				else if (d is Dy)
				{
					if (d.Data > my)
					{
						my = d.Data;
					}
				}
				else if (d is Dc)
				{
					if (d.Data > mc)
					{
						mc = d.Data;
					}
				}
			}

			//l("Max value for x: " + mx);
			byte rbx = GetBitCount(mx);
			//l("Selected bit count for x: " + rbx);
			//l("Max value for y: " + my);
			byte rby = GetBitCount(my);
			//l("Selected bit count for y: " + rby);
			//l("Max value for c: " + mc);
			byte rbc = GetBitCount(mc);
			//l("Selected bit count for c: " + rbc);
			frb = 8;

			// write header
			// $0-1 - Size of Uncompressed/Unpacked Art.
			result.AddRange(ByteConverter.GetBytes(sz));

			// $2 - Compression Marker [Always $42]
			result.Add(0x42);

			// $3 - Initial X/Base X/Left Most possible X from Center Point.
			result.Add(rx);

			// $4 - Bitcount for X.
			result.Add(rbx);

			// $5 - Initial Y/Base Y/Top Most possible Y from Center Point.
			result.Add(ry);

			// $6 - Bitcount for Y.
			result.Add(rby);

			// $7 - Base Palette Color Index [Usually $00 unless you are not setting it in the ArtQueue Load]
			result.Add(0);

			// $8 - Bitcount for Palette Color Entries.
			result.Add(rbc);

			// Final X Pixel Location[Sets Initial X Prior to calling BitStream]
			result = WriteBitStream(result, rbx, w);
			// Final Y Pixel Location [Sets Initial Y Prior to calling BitStream]
			result = WriteBitStream(result, rby, h);

			// remove first 2 things
			data.RemoveRange(0, 2);

			// loop until end token
			while (true)
			{
				// get row data start and end
				byte xst = (byte)data[0].Data, xnd = (byte)data[1].Data;
				if (xst == xnd)
				{
					int x = 0;
				}

				result = WriteBitStream(result, rbx, xst);
				result = WriteBitStream(result, rbx, xnd);

				// remove first 2 entries we used
				data.RemoveRange(0, 2);

				// if start and end are same, end
				if (xst == xnd)
				{
					break;
				}

				// get y-offset of the row
				byte y = (byte)data[0].Data;
				result = WriteBitStream(result, rby, y);

				// remove the entry we used
				data.RemoveAt(0);

				// calculate pixels in the line
				int n = xnd - xst;

				// check if xst is less than xnd
				if (n < 0)
				{
					//e("Unable to pack; row start '" + xst + "' is more than row end '" + xnd + "'!");
					//return;
				}

				// loop for all pixels on that row
				for (; n != 0; n--)
				{
					result = WriteBitStream(result, rbc, (byte)data[0].Data);

					// remove the entry we used
					data.RemoveAt(0);
				}
			}

			result = WriteBitStream(result, 9 - frb, 0);

			if (result.Count % 2 != 0)
				result.Add(0);

			return result.ToArray();
		}

		private static byte ReadBitStream(int shift, byte[] data, ref int index)
		{
			byte o = 0; // will be the final destination of bits

			// loops until all bits are populated
			while (--shift >= 0)
			{
				// fills sb with next byte if frb (available bits) is 0
				if (frb == 0)
				{
					byte b = data[index++];

					// reverse bit order of b to sb. This helps to later roll bits to o easier
					sb = (byte)(((b & 0x1) << 7) |
						((b & 0x2) << 5) |
						((b & 0x4) << 3) |
						((b & 0x8) << 1) |
						((b & 0x10) >> 1) |
						((b & 0x20) >> 3) |
						((b & 0x40) >> 5) |
						((b & 0x80) >> 7));

					frb = 8;
				}

				// then insert a bit from sb to correct place
				o |= (byte)((sb & 1) << shift);

				// then shift sb to get rid of the last bit we used
				sb >>= 1;

				// finally tell frb we used a bit
				frb--;
			}

			return o;
		}

		private static List<byte> WriteBitStream(List<byte> resultData, int bits, int data)
		{
			// reverse bit order of data. This helps to later roll bits easier
			data = ((data & 0x1) << 7) |
				((data & 0x2) << 5) |
				((data & 0x4) << 3) |
				((data & 0x8) << 1) |
				((data & 0x10) >> 1) |
				((data & 0x20) >> 3) |
				((data & 0x40) >> 5) |
				((data & 0x80) >> 7);

			data >>= 8 - bits;

			// loops until all bits are populated
			while (--bits >= 0)
			{
				// writes sb if frb (available bits) is 0
				if (frb == 0)
				{
					// write it
					resultData.Add(sb);
					// reset vars
					frb = 8;
					sb = 0;
				}

				sb <<= 1;
				// then insert a bit to sb
				sb |= (byte)(data & 1);

				// shift out the used bit from input
				data >>= 1;

				// finally tell frb we used a bit
				frb--;
			}

			return resultData;
		}

		private static byte GetBitCount(long x)
		{
			for (byte i = 1, z = 1; i <= 8; i++, z = (byte)((z << 1) | 1))
			{
				if (x == (x & z))
				{
					return i;
				}
			}

			//e("Unable to find bitcount! Max value of " + x + " can not be represented in 8 bits!");
			return 8;
		}

		private static short SignedExtendByteToWord(byte b)
		{
			if ((b & 0x80) == 0)
			{
				return b;

			}
			else
			{
				return (short)(0xFF00 | b);
			}
		}

		#endregion
	}
}
