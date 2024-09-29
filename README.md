# JavaSerializer

Software library to parse Java serialized content.

This is a fork of https://github.com/GitKepler/JavaSerializer

## Usage

Refer to project `test1`:

```c#
using (var stream = File.OpenRead("test1.bin"))
using (var reader = new JavaSerializer.SerializedStreamReader(stream))
{
	reader.Read();
	var ct = reader.Content[0];
	if (ct is ObjectContent obj && (obj.ClassDescriptor as ClassDescriptorContent).ClassName == "Data1")
	{
		var rv = ReadData1(obj);
		Console.WriteLine(rv);
	}
}
```

## Protocol Spec

https://docs.oracle.com/javase/8/docs/platform/serialization/spec/protocol.html

Simplified spec:
```spec
content:
	object
	blockdata

newObject:
	TC_OBJECT classDesc newHandle classdata[]  // data for each class

classdata:
	nowrclass
	wrclass objectAnnotation
	objectAnnotation

objectAnnotation:
	contents endBlockData
```

## ChangLog

- dotnet standard 2.0
- bugfix: byte order of float/double data
- bugfix: read flags (ClassDescFlag)
- support fields of types like `List<double[]>`.
implement objectAnnatation grammer.

- add java test project `jtest1` and C# demo project `test1`. `jtest1` write data file `test1.bin` and `test1` read from the data file.

# 开发说明

- 文件头`aced`是java序列化文件的标志，接着`0005`是版本
- blockdata只有标识、长度和数据，没有类型，比如`77 0a`中`77`标识blockdata(short)，`0a`表示len=10, 后面直接跟bytes，必须看源码才能知道这里是什么数据、如何解析。
- int, float, double等数据全部保存为大端字节序。

## List是如何存储的

在保存Ojbect对象时，先是类型（类名、字段列表等），然后在classdata[]中是对应字段的一个个数据，但要注意每个数据后面可以跟一个可选的objectAnnotation数据（这个数据又可以是任何contents）。
尤其是对于`IList<float[]>`这样的类型（Java中实际类型为ArrayList或LinkedList；反序列化到c#中用List保存），其数据主要是保存在objectAnnotation中。

ArrayList/LinkedList中先是有个Integer size的字段

以JSON格式来理解：
```txt
{  // ObjectContent:IContent
	ClassDescriptor: {  // ClassDescriptorContent:IClassDescriptor
		ClassName: "Data1",
		Fields: [  // IList<IClassField>
			{  // PrimitiveField:IClassField
				Name: "ival",
				Type: FieldType.Integer
			},
			{  // ObjectField:IClassField
				Name: "X",
				Type: FieldType.Object,
				UnderlyingType: { // UtfStringContent, ObjectField才有
					String: "Ljava/util/List;"
					FinalString: "Ljava/util/List;" // TODO?
				}
			},
			{
				Name: "Y",
				Type: FieldType.Object,
				UnderlyingType: { // StringReference, 注意它会引用上面X定义过的类型
					String: "Ljava/util/List;"
					PointerValue: "X"的类型,
					Value: "X"的类型（TODO：与PointerValue重复？)
				}
			},
			{ // ObjectField:IClassField
				Name: "buf",
				Type: FieldType.Array,
				UnderlyingType: {
					String: "[B" // byte[]
				}
			},
			{ // ObjectField:IClassField
				Name: "doubleArr",
				Type: FieldType.Array,
				UnderlyingType: {
					String: "[D"  // double[]
				}
			},
			{ // ObjectField:IClassField
				Name: "map",
				Type: FieldType.Object,
				UnderlyingType: {
					String: "Ljava/util/Map;"
				}
			},
			{ // ObjectField:IClassField
				Name: "name",
				Type: FieldType.Object,
				UnderlyingType: {
					String: "Ljava/lang/String;"
				}
			},
		],
	},
	Values: {  // field:string => value:object, 对应ClassDescriptor.Fields中每个字段
		"ival": 0x99,  // object{int}

		"X": {  // object{ObjectContent}
			ClassDescriptor: {
				ClassName: "java.util.ArrayList",
				Fields: [
					{ // PrimitiveField:IClassField
						Name: "size",
						Type: FieldType.Integer
					}
				]
			},
			Values: {
				"size": 2 // object{int}
			},
			Annotations: [  // List<IContent>
				{ // BlockDataContent:IContent
					Size: 4,
					Data: [0,0,0,2]  // byte[]
				},
				{ // ArrayContent:IContent
					ClassDescriptor: { // ClassDescriptorContent
						ClassName: "[I"
					}
					Data: [
						0x61626364, 0x65666768
					],
				}
				...
			]
		},

		"Y": {  // object{ObjectContent}
			ClassDescriptor: {
				ClassName: "java.util.LinkedList",
				Fields: null
			},
			Values: {
			},
			Annotations: [  // List<IContent>
				{ // BlockDataContent:IContent
					Size: 4,
					Data: [0,0,0,2]  // byte[] 猜测为个数，后面再接2个Annotations
				},
				{ // ArrayContent:IContent
					ClassDescriptor: { // ClassDescriptorContent
						ClassName: "[F" // float[]
					}
					Data: [ 11.1, 22.2, 33.3, 44.4 ],
				}
				...
			]
		},

		"buf": {
			ClassDescriptor: {
				ClassName: "[B",  // 表示byte[]
			},
			Data: {
				0x41, 0x42, 0x43, 0x44
			}
		},

		"doubleArr": {
			ClassDescriptor: {
				ClassName: "[D",  // 表示double[]
			},
			Data: {
				1.1, 2.2, 3.3, 4.4
			}
		},

		"map": {
			ClassDescriptor: {
				ClassName: "java.util.HashMap",
				Fields: [
					{ Name: "loadFactor", Type: FieldType.Float }, // PrimitiveField
					{ Name: "threshold", Type: FieldType.Integer }, // PrimitiveField
				]
			},
			Values: {
				"loadFactor": ...,
				"threshold": 12,
			},
			Annatations: [  // IList<IContent>
				{ // BlockDataContent
					Size: 8,
					Data: [0,0,0,0x10, 0,0,0,3], // 猜测第2个int(值为3)是元素个数，决定了Annotations下面还有3组数据(key-value)共6个
				},
				{ // UtfStringContent
					String: "key1",
				},
				{ // ObjectContent
					ClassDescriptor: {
						ClassName: "java.lang.Boolean",
						Fields: [
							{Name: "value", Type: FieldType.Boolean}
						]
					},
					Values: {
						"value": true // object{bool}
					}
				}
				... // "key2", false, "key3", true
			]
		}
	}
}
```

## 关于prevObject即类型引用

对于相同类型，bin文件中可能会引用之前定义过的类型。代码中使用_handleMapping进行保存和引用。
关于newHandle, handle(int)的理解

prevObject
  TC_REFERENCE (int)handle

以上表示对之前一个对象（或类型）的引用，示例"\x75(TC_ARRAY)\x71(TC_REFERENCE) \x00\x7e \x00\x08" (uq开头)
handle的值为"\x00\x7e \x00\x08".
每个表达式中有newHandle的地址，就是在基址"\x00\x7e\x00\00"上加1，所以\x08相当于是第9个对象。

