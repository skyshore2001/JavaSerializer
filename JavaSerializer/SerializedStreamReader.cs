﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JavaSerializer.Content;
using JavaSerializer.Content.BlockData;
using JavaSerializer.Content.Interface;
using JavaSerializer.Content.Object;
using JavaSerializer.Content.Object.ClassDesc;
using JavaSerializer.Content.Object.ClassDesc.FieldDescriptor;
using JavaSerializer.Content.Object.ClassDesc.FieldDescriptor.Interface;
using JavaSerializer.Content.Object.Extensions;
using JavaSerializer.Content.Object.Inteface;

namespace JavaSerializer
{
    public class SerializedStreamReader : IDisposable
    {
        private readonly BinaryReader _reader;

        public ushort Version { get; private set; }
        public IReadOnlyList<IContent> Content => _content;
        private readonly List<IContent> _content = new();

        public SerializedStreamReader(Stream stream)
        {
            _reader = new BinaryReader(stream, Encoding.UTF8);
        }

        public void Read()
        {
            var magic = _reader.ReadUInt16BE();
            if (magic != 0xACED) throw new InvalidDataException($"Invalid magic number: Received 0x{magic:X4}, Expected 0xACED.");
            Version = _reader.ReadUInt16BE();

            while (ReadContent<IContent>(out var content, false, TokenType.TC_ENDBLOCKDATA) && content is not null)
            {
                _content.Add(content);
            }
        }

        private readonly List<IContent> _handleMapping = new();
        public IReadOnlyList<IContent> HandleMapping => _handleMapping;

        private const uint HandleOffset = 0x007E0000;

        private bool ReadContent<TData>(out TData? content, bool whitelistRestriction, params TokenType[] restrictions) where TData : class, IContent
        {
            TokenType contentType;
            try
            {
                contentType = _reader.ReadTokenType();
            }
            catch (EndOfStreamException)
            {
                content = null;
                return false;
            }

            if (restrictions is not null && (whitelistRestriction ^ restrictions.Contains(contentType)))
                throw new InvalidDataException($"An invalid content has been read. Fetched content type = {contentType}.");

            IContent parsedData;

            switch (contentType)
            {
                case TokenType.TC_OBJECT: // done
                    var objectContent = new ObjectContent(contentType);
                    ReadObject(objectContent);
                    parsedData = objectContent;
					var c = _reader.PeekChar();
                    if (c == (byte)TokenType.TC_BLOCKDATA)
					{
						while (ReadContent<IContent>(out var ct, false) && ct != null && ct.Header != TokenType.TC_ENDBLOCKDATA)
						{
							objectContent.Annotations.Add(ct);
						}
					}
                    break;
                case TokenType.TC_CLASS: // done
                    var classContent = new ClassContent(contentType);
                    ReadClass(classContent);
                    parsedData = classContent;
                    break;
                case TokenType.TC_ARRAY: // done
                    var arrayContent = new ArrayContent(contentType);
                    ReadArray(arrayContent);
                    parsedData = arrayContent;
                    break;
                case TokenType.TC_LONGSTRING: // done
                case TokenType.TC_STRING: // done
                    var stringContent = new UtfStringContent(contentType);
                    ReadString(stringContent);
                    parsedData = stringContent;
                    break;
                case TokenType.TC_ENUM: // done
                    var enumContent = new EnumContent(contentType);
                    ReadEnum(enumContent);
                    parsedData = enumContent;
                    break;
                case TokenType.TC_CLASSDESC: // done
                    var classDescContent = new ClassDescriptorContent(contentType);
                    ReadClassDescriptorContent(classDescContent);
                    parsedData = classDescContent;
                    break;
                case TokenType.TC_PROXYCLASSDESC: // done
                    var proxyDescContent = new ProxyClassDescContent(contentType);
                    ReadProxyClassDescriptor(proxyDescContent);
                    parsedData = proxyDescContent;
                    break;
                case TokenType.TC_REFERENCE: // done
                    var handleValue = (int)(_reader.ReadUInt32BE() - HandleOffset);
                    var referenceValue = HandleMapping[handleValue];
                    parsedData = referenceValue switch
                    {
                        IString stringReference => new StringReference(contentType, stringReference),
                        IClassDescriptor classDescriptor => new ClassDescriptorReference(contentType, classDescriptor),
                        IObject freeReference => new FreeReference(contentType, freeReference),
                        null => throw new InvalidDataException("Null reference parsed."),
                        _ => throw new InvalidDataException($"Unknown reference type parsed: {referenceValue.GetType()}")
                    };
                    break;
                case TokenType.TC_NULL: // done
                    parsedData = new NullReferenceContent(contentType);
                    break;
                case TokenType.TC_EXCEPTION:
                    parsedData = new ExceptionContent(contentType);
                    break;
                case TokenType.TC_RESET: // done
                    parsedData = new ResetContent(contentType);
                    break;
                case TokenType.TC_BLOCKDATA: // done
                    var blockDataContent = new BlockDataContent(contentType);
                    blockDataContent.Size = _reader.ReadByte();
                    blockDataContent.Data = _reader.ReadBytes(blockDataContent.Size);
                    parsedData = blockDataContent;
                    break;
                case TokenType.TC_BLOCKDATALONG: // done
                    var blockDataLongContent = new BlockDataContent(contentType);
                    blockDataLongContent.Size = _reader.ReadInt32BE();
                    blockDataLongContent.Data = _reader.ReadBytes(blockDataLongContent.Size);
                    parsedData = blockDataLongContent;
                    break;
                case TokenType.TC_ENDBLOCKDATA: // done
                    parsedData = new EndBlockDataContent(contentType);
                    break;
                default: throw new InvalidDataException($"Invalid content type received: {contentType}");
            }

            if (parsedData is not TData castedContent) throw new InvalidDataException($"The parsed data is not of the correct type: Expected type is {typeof(TData)}, received type is {parsedData.GetType()}");

            content = castedContent;

            return true;
        }

        private IClassDescriptor ReadClassDescriptor(IObjectWithClassDescriptor objectWithClassDescriptor)
        {
            _ = ReadContent<IClassDescriptor>(out var classDescriptor, true, TokenType.TC_CLASSDESC, TokenType.TC_PROXYCLASSDESC, TokenType.TC_NULL, TokenType.TC_REFERENCE);
            if (classDescriptor is null) throw new EndOfStreamException();
            objectWithClassDescriptor.ClassDescriptor = classDescriptor;

            return classDescriptor;
        }

        private T[] ReadArray<T>(int size, Func<T> readFn)
		{
            var res = new T[size];
			for (int i = 0; i < size; i++)
			{
                res[i] = readFn();
			}
            return res;
		}
        private void ReadArray(ArrayContent content)
        {
            var classDescriptor = ReadClassDescriptor(content);
            _handleMapping.Add(content);

            int size = _reader.ReadInt32BE();
            while (classDescriptor?.Header == TokenType.TC_REFERENCE && classDescriptor is ClassDescriptorReference reference)
            {
                classDescriptor = reference.Value;
            }

            if (classDescriptor?.Header == TokenType.TC_NULL) throw new InvalidDataException("The class descriptor is a null reference.");
            if (classDescriptor?.Header == TokenType.TC_PROXYCLASSDESC) throw new InvalidDataException("Proxy class descriptors are not supported when fetching class members.");

            if (classDescriptor?.Header != TokenType.TC_CLASSDESC || classDescriptor is not ClassDescriptorContent finalClassDescriptor) throw new InvalidDataException($"The object is not a class descriptor but a {classDescriptor?.Header}.");

            FieldType fieldType = FieldType.None;
            if (finalClassDescriptor.ClassName?.Length == 2 && finalClassDescriptor.ClassName[0] == (byte)FieldType.Array)
            {
                var fieldTypeValue = (byte)finalClassDescriptor.ClassName[1];
                if (!Enum.IsDefined(typeof(FieldType), fieldTypeValue)) throw new InvalidDataException($"Unknown field type: {finalClassDescriptor.ClassName}");
                fieldType = (FieldType)fieldTypeValue;
            }

			content.Data = fieldType switch
			{
				FieldType.Byte => ReadArray<byte>(size, () => _reader.ReadByte()),
				FieldType.Char => ReadArray<char>(size, () => _reader.ReadChar()),
				FieldType.Double => ReadArray<double>(size, () => ReverseByte(_reader.ReadDouble())),
				FieldType.Float => ReadArray<float>(size, () => ReverseByte(_reader.ReadSingle())),
				FieldType.Integer => ReadArray(size, () => _reader.ReadInt32BE()),
				FieldType.Long => ReadArray<long>(size, () => _reader.ReadInt64BE()),
				FieldType.Short => ReadArray<short>(size, () => _reader.ReadInt16BE()),
				FieldType.Boolean => ReadArray<bool>(size, () => _reader.ReadBoolean()),
				FieldType.None => ReadArray<object>(size, () =>
				{
                    _ = ReadContent<IContent>(out var resultingObject, true, TokenType.TC_NULL, TokenType.TC_REFERENCE, TokenType.TC_ARRAY, TokenType.TC_OBJECT, TokenType.TC_STRING, TokenType.TC_LONGSTRING);
                    if (resultingObject is null) throw new EndOfStreamException();
                    return resultingObject;
				}),
				FieldType.Array or FieldType.Object => throw new InvalidDataException($"A {fieldType} is not a primitive field type."),
				_ => throw new InvalidDataException($"Unknown value provided ({fieldType})")
			};
        }

        // n=2,4,8
        public static unsafe void ReverseByte(byte *p, int n)
		{
            for (int i=0; i<n/2; ++i)
			{
                byte c = p[i];
                p[i] = p[n - i - 1];
                p[n - i - 1] = c;
			}
		}
        public static unsafe double ReverseByte(double v)
		{
            ReverseByte((byte*)&v, sizeof(double));
            return v;
		}
        public static unsafe float ReverseByte(float v)
		{
            ReverseByte((byte*)&v, sizeof(float));
            return v;
		}
        public static unsafe int ReverseByte(int v)
		{
            ReverseByte((byte*)&v, sizeof(int));
            return v;
		}

        private void ReadObject(ObjectContent content)
        {
            var classDescriptor = ReadClassDescriptor(content);
            _handleMapping.Add(content);

            foreach(var field in GetClassFieldsFromClassDescriptor(classDescriptor))
            {
                if(field is PrimitiveField primitiveField)
                {
                    content.Values[field.Name] = primitiveField.Type switch
                    {
                        FieldType.Byte => _reader.ReadByte(),
                        FieldType.Char => _reader.ReadChar(),
						FieldType.Double => ReverseByte(_reader.ReadDouble()),
						FieldType.Float => ReverseByte(_reader.ReadSingle()),
                        FieldType.Integer => _reader.ReadInt32BE(),
                        FieldType.Long => _reader.ReadInt64BE(),
                        FieldType.Short => _reader.ReadInt16BE(),
                        FieldType.Boolean => _reader.ReadBoolean(),
                        FieldType.Array or FieldType.Object => throw new InvalidDataException($"A {primitiveField.Type} is not a primitive field type."),
                        _ => throw new InvalidDataException($"Unknown value provided ({primitiveField.Type})")
                    };
                }
                else if(field is ObjectField objectField)
                {
                    _ = ReadContent<IContent>(out var resultingObject, true, TokenType.TC_NULL, TokenType.TC_REFERENCE, TokenType.TC_ARRAY, TokenType.TC_OBJECT, TokenType.TC_LONGSTRING, TokenType.TC_STRING);
                    if (resultingObject is null) throw new EndOfStreamException();

                    content.Values[field.Name] = resultingObject;
                }
                else
                {
                    throw new InvalidDataException("The provided object is not valid (unknown field type)");
                }
            }
        }

        private IReadOnlyList<IClassField> GetClassFieldsFromClassDescriptor(IClassDescriptor? classDescriptorOrPointer)
        {
            var fields = new List<IList<IClassField>>();

            while(classDescriptorOrPointer?.Header == TokenType.TC_REFERENCE && classDescriptorOrPointer is ClassDescriptorReference reference)
            {
                classDescriptorOrPointer = reference.Value;
            }

            if (classDescriptorOrPointer?.Header == TokenType.TC_NULL) throw new InvalidDataException("The class descriptor is a null reference.");
            if (classDescriptorOrPointer?.Header == TokenType.TC_PROXYCLASSDESC) throw new InvalidDataException("Proxy class descriptors are not supported when fetching class members.");

            if (classDescriptorOrPointer?.Header != TokenType.TC_CLASSDESC || classDescriptorOrPointer is not ClassDescriptorContent classDescriptor) throw new InvalidDataException($"The object is not a class descriptor but a {classDescriptorOrPointer?.Header}.");

            if(classDescriptor.Fields is not null)
                fields.Add(classDescriptor.Fields);

            var superClassDescriptor = classDescriptor.ClassDescriptor;

            while (superClassDescriptor?.Header == TokenType.TC_REFERENCE || superClassDescriptor?.Header == TokenType.TC_CLASSDESC)
            {
                while(superClassDescriptor?.Header == TokenType.TC_REFERENCE && superClassDescriptor is ClassDescriptorReference referenceSuper)
                {
                    superClassDescriptor = referenceSuper.Value;
                }

                if(superClassDescriptor?.Header == TokenType.TC_CLASSDESC && superClassDescriptor is ClassDescriptorContent classDescriptorSuperClass)
                {
                    if (classDescriptorSuperClass.Fields is not null)
                        fields.Add(classDescriptorSuperClass.Fields);
                    classDescriptor = classDescriptorSuperClass;
                    superClassDescriptor = classDescriptorSuperClass.ClassDescriptor;
                }
                else
                {
                    throw new InvalidDataException($"A class has a superclass with a non-classdesc/reference entity: class \"{classDescriptor.ClassName}\", super class type: {superClassDescriptor?.Header.ToString() ?? "null"}.");
                }
            }

            fields.Reverse();
            return fields.SelectMany(x => x).ToList();
        }

        private void ReadString(UtfStringContent content)
        {
            _handleMapping.Add(content);
            content.String = content.Header switch
            {
                TokenType.TC_STRING => _reader.ReadUInt16String(),
                TokenType.TC_LONGSTRING => _reader.ReadInt64String(),
                _ => throw new InvalidDataException($"The string element has an invalid header ({content.Header})."),
            };
        }

        private void ReadEnum(EnumContent content)
        {
            ReadClassDescriptor(content);

            _handleMapping.Add(content);

            _ = ReadContent<IString>(out var stringContent, true, TokenType.TC_STRING, TokenType.TC_LONGSTRING, TokenType.TC_REFERENCE);
            if (stringContent is null) throw new EndOfStreamException();
            content.EnumConstantName = stringContent;
        }

        private void ReadClass(ClassContent content)
        {
            ReadClassDescriptor(content);

            _handleMapping.Add(content);
        }

        private void ReadProxyClassDescriptor(ProxyClassDescContent content)
        {
            _handleMapping.Add(content);

            var interfaceNameCount = _reader.ReadUInt32BE();
            if(interfaceNameCount > 0)
            {
                content.InterfaceNames = new List<string>();
                for(int i = 0; i < interfaceNameCount; i++)
                {
                    content.InterfaceNames.Add(_reader.ReadUInt16String());
                }
            }

            while (ReadContent<IContent>(out var annotation, false /* Allow all types */) && annotation is not EndBlockDataContent && annotation is not null)
            {
                content.Annotations ??= new List<IContent>();
                content.Annotations.Add(annotation);
            }

            ReadClassDescriptor(content);
        }

        private void ReadClassDescriptorContent(ClassDescriptorContent content)
        {
            content.ClassName = _reader.ReadUInt16String();
            content.SerialVersionUID = _reader.ReadUInt64BE();
            _handleMapping.Add(content);

            content.ClassDescFlag = _reader.ReadClassDescFlags();
            var fieldCount = _reader.ReadUInt16BE();
            if (fieldCount > 0)
            {
                content.Fields = new List<IClassField>();
                for (int i = 0; i < fieldCount; i++)
                {
                    content.Fields.Add(ReadField());
                }
            }

            while(ReadContent<IContent>(out var annotation, false /* Allow all types */) && annotation is not EndBlockDataContent && annotation is not null)
            {
                content.Annotations ??= new List<IContent>();
                content.Annotations.Add(annotation);
            }

            ReadClassDescriptor(content);
        }

        private IClassField ReadField()
        {
            var fieldType = _reader.ReadFieldType();
            var fieldName = _reader.ReadUInt16String();
            if(fieldType == FieldType.Array || fieldType == FieldType.Object)
            {
                _ = ReadContent<IString>(out var stringContent, true, TokenType.TC_STRING, TokenType.TC_LONGSTRING, TokenType.TC_REFERENCE);
                if (stringContent is null) throw new EndOfStreamException();

                return new ObjectField(fieldType, fieldName, stringContent);
            }

            return new PrimitiveField(fieldType, fieldName);
        }

        public void Dispose()
        {
            _reader.Dispose();
        }
    }
}
