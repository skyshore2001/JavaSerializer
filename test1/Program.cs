using JavaSerializer.Content.BlockData;
using JavaSerializer.Content.Interface;
using JavaSerializer.Content.Object;
using System;
using System.Collections.Generic;
using System.IO;

namespace test1
{
    class Program
    {
        static void Main(string[] args)
        {
			string binFile = @"../../../jtest1/test1.bin";
			using (var stream = File.OpenRead(binFile))
			using (var reader = new JavaSerializer.SerializedStreamReader(stream))
			{
				reader.Read();
				var ct = reader.Content[0];
				if (ct is ObjectContent obj && (obj.ClassDescriptor as JavaSerializer.Content.Object.ClassDesc.ClassDescriptorContent).ClassName == "Data1")
				{
					var rv = ReadData1(obj);
					Console.WriteLine(rv);
				}
			}
        }

        static Data1 ReadData1(ObjectContent obj)
		{
			var res = new Data1();
			object v = null;
			v = obj.Values["ival"];
			res.ival = (int)v;
			v = obj.Values["name"];
			res.name = (v as UtfStringContent).String;
			v = obj.Values["buf"];
			res.buf = (byte[])(v as ArrayContent).Data;
			v = obj.Values["doubleArr"];
			res.doubleArr = (double[])(v as ArrayContent).Data;
			v = obj.Values["X"];
			res.X = ReadArrList<int>(v as ObjectContent);
			v = obj.Values["Y"];
			res.Y = ReadArrList<float>(v as ObjectContent);
			v = obj.Values["map"];
			res.map = ReadMap<bool>(v as ObjectContent);
			return res;
		}
		static List<T[]> ReadArrList<T>(ObjectContent v)
		{
			var res = new List<T[]>();
			var cnt = BitConverter.ToInt32((v.Annotations[0] as BlockDataContent).Data as byte[], 0);
			cnt = JavaSerializer.SerializedStreamReader.ReverseByte(cnt);
			for (int i=1; i<=cnt; ++i)
			{
				var arr = (T[])(v.Annotations[i] as ArrayContent).Data;
				res.Add(arr);
			}
			return res;
		}
		static Dictionary<string, T> ReadMap<T>(ObjectContent v)
		{
			var res = new Dictionary<string, T>();
			var cnt = BitConverter.ToInt32((v.Annotations[0] as BlockDataContent).Data as byte[], 4); // 0-4 may be capacity, 4-8 is count
			cnt = JavaSerializer.SerializedStreamReader.ReverseByte(cnt);
			for (int i = 1; i <= cnt*2; i += 2)
			{
				var k = v.Annotations[i] as UtfStringContent;
				var v1 = v.Annotations[i + 1];
				if (v1 is ObjectContent v2)
				{
					res.Add(k.String, (T)v2.Values["value"]);
				}
				else if (v1 is JavaSerializer.Content.Object.Extensions.FreeReference v3)
				{
					var v4 = v3.PointerValue as ObjectContent;
					res.Add(k.String, (T)v4.Values["value"]);
				}
			}
			return res;
		}
    }

    class Data1
    {
		public int ival;
		public string name;
		public byte[] buf;
		public double[] doubleArr;
		public IList<int[]> X;
		public IList<float[]> Y;
		public Dictionary<string, bool> map;
	}
}
