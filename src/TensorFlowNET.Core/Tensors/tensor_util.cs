﻿/*****************************************************************************
   Copyright 2018 The TensorFlow.NET Authors. All Rights Reserved.

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
******************************************************************************/

using Tensorflow.NumPy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tensorflow.Eager;
using Tensorflow.Graphs;
using static Tensorflow.Binding;

namespace Tensorflow
{
    public static class tensor_util
    {
        public static TF_DataType[] _TENSOR_CONTENT_TYPES =
        {
            TF_DataType.TF_FLOAT, TF_DataType.TF_DOUBLE, TF_DataType.TF_INT32, TF_DataType.TF_UINT8, TF_DataType.TF_INT16,
            TF_DataType.TF_INT8, TF_DataType.TF_INT64, TF_DataType.TF_QINT8, TF_DataType.TF_QUINT8, TF_DataType.TF_QINT16,
            TF_DataType.TF_QUINT16, TF_DataType.TF_QINT32, TF_DataType.TF_UINT32, TF_DataType.TF_UINT64
        };

        /// <summary>
        /// Returns the constant value of the given tensor, if efficiently calculable.
        /// </summary>
        /// <param name="tensor"></param>
        /// <param name="partial"></param>
        /// <returns></returns>
        public static NDArray constant_value(Tensor tensor, bool partial = false)
        {
            if (tensor is EagerTensor)
                return tensor.numpy();

            NDArray ret = _ConstantValue(tensor, partial);
            if (!(ret is null))
                tensor.graph.prevent_feeding(tensor);

            return ret;
        }

        private static NDArray _ConstantValue(Tensor tensor, bool partial)
        {
            switch (tensor.op.type)
            {
                case "Const":
                    return MakeNdarray(tensor.op.get_attr("value") as TensorProto);
                default:
                    return null;
            }
        }

        public static NDArray MakeNdarray(TensorProto tensor)
        {
            var shape = tensor.TensorShape.Dim.Select(x => (int)x.Size).ToArray();
            int num_elements = np.prod(shape);
            var tensor_dtype = tensor.Dtype.as_numpy_dtype();

            if (shape.Length > 0 && tensor.TensorContent.Length > 0)
            {
                return np.frombuffer(tensor.TensorContent.ToByteArray(), tensor_dtype).reshape(shape);
            }
            else if (tensor.Dtype == DataType.DtHalf || tensor.Dtype == DataType.DtBfloat16)
            {
                return np.array(tensor.HalfVal.ToArray()).reshape(shape);
            }
            else if (tensor.Dtype == DataType.DtFloat)
            {
                return np.array(tensor.FloatVal.ToArray()).reshape(shape);
            }
            else if (new DataType[] { DataType.DtInt32, DataType.DtUint8 }.Contains(tensor.Dtype))
            {
                return np.array(tensor.IntVal.ToArray()).reshape(shape);
            }
            else if (tensor.Dtype == DataType.DtBool)
            {
                return np.array(tensor.BoolVal.ToArray()).reshape(shape);
            }

            throw new NotImplementedException("MakeNdarray");
        }

        private static readonly TF_DataType[] quantized_types = new TF_DataType[]
        {
            TF_DataType.TF_QINT8, TF_DataType.TF_QUINT8, TF_DataType.TF_QINT16, TF_DataType.TF_QUINT16,
            TF_DataType.TF_QINT32
        };

        /// <summary>
        /// Create a TensorProto, invoked in graph mode
        /// </summary>
        /// <param name="values"></param>
        /// <param name="dtype"></param>
        /// <param name="shape"></param>
        /// <param name="verify_shape"></param>
        /// <param name="allow_broadcast"></param>
        /// <returns></returns>
        public static TensorProto make_tensor_proto(object values, TF_DataType dtype = TF_DataType.DtInvalid, int[]? shape = null, bool verify_shape = false, bool allow_broadcast = false)
        {
            if (allow_broadcast && verify_shape)
                throw new ValueError("allow_broadcast and verify_shape are not both allowed.");
            if (values is TensorProto tp)
                return tp;

            dtype = values.GetType().as_tf_dtype();
            var tensor_proto = new TensorProto
            {
                Dtype = dtype.as_datatype_enum(),
            };

            // scalar
            if (!values.GetType().IsArray)
            {
                tensor_proto.TensorShape = tensor_util.as_shape(new int[0]);

                switch (values)
                {
                    case bool val:
                        tensor_proto.BoolVal.AddRange(new[] { val });
                        break;
                    case int val:
                        tensor_proto.IntVal.AddRange(new[] { val });
                        break;
                    case long val:
                        tensor_proto.Int64Val.AddRange(new[] { val });
                        break;
                    case float val:
                        tensor_proto.FloatVal.AddRange(new[] { val });
                        break;
                    case double val:
                        tensor_proto.DoubleVal.AddRange(new[] { val });
                        break;
                    case string val:
                        tensor_proto.StringVal.AddRange(val.Select(x => Google.Protobuf.ByteString.CopyFromUtf8(x.ToString())));
                        break;
                    default:
                        throw new Exception("make_tensor_proto Not Implemented");
                }

                return tensor_proto;
            }
            else if (dtype == TF_DataType.TF_STRING && !(values is NDArray))
            {
                if (values is string str)
                {
                    tensor_proto.StringVal.Add(Google.Protobuf.ByteString.CopyFromUtf8(str));
                    tensor_proto.TensorShape = tensor_util.as_shape(new int[0]);
                }
                else if (values is string[] str_values)
                    tensor_proto.StringVal.AddRange(str_values.Select(x => Google.Protobuf.ByteString.CopyFromUtf8(x)));
                else if (values is byte[] byte_values)
                    tensor_proto.TensorContent = Google.Protobuf.ByteString.CopyFrom(byte_values);

                return tensor_proto;
            }
            else
            {
                tensor_proto.TensorShape = tensor_util.as_shape(shape);

                // array
                if (_TENSOR_CONTENT_TYPES.Contains(dtype))
                {
                    throw new NotImplementedException("");
                    /*byte[] bytes = nparray.ToByteArray();
                    tensor_proto.TensorContent = Google.Protobuf.ByteString.CopyFrom(bytes.ToArray());
                    return tensor_proto;*/
                }
            }

            return tensor_proto;
        }

        public static TensorShape constant_value_as_shape(Tensor tensor)
        {
            bool hasattr(Graph property, string attr)
            {
                var t = property.GetType().GetProperties();
                foreach (System.Reflection.PropertyInfo pi in t)
                {
                    if (pi.Name == attr)
                        return true;
                }
                return false;
            }

            if (tensor.GetType() == typeof(EagerTensor))
            {
                return new TensorShape(tensor.numpy().ToArray<int>());
            }

            if (tensor.TensorShape.ndim == 0)
            {
                var value_ = constant_value(tensor);
                if (value_ == null)
                    throw new ValueError(
                        @"Received a scalar with unknown value as shape; require a statically
known scalar with value '-1' to describe an unknown shape.");
                if (value_ != -1)
                    throw new ValueError(
                        String.Format(@"Received a scalar value {0} as shape; require a statically known
scalar with value '-1' to describe an unknown shape.", value_));
                return tensor.TensorShape.unknown_shape(-1);
            }

            var shape = tensor.TensorShape.with_rank(1);
            if (shape == new TensorShape(new int[] { 1 }))
            {
                return new TensorShape(new int[] { });
            }
            else if (tensor.op.type == "Cast")
            {
                var pre_cast = constant_value_as_shape(tensor.op.inputs[0]);
                if (pre_cast.dims == null)
                    return pre_cast;
                var cast_dtype = dtypes.as_tf_dtype((Type)tensor.op.get_attr("DstT"));
                if (!Array.Exists(new[] { dtypes.int32, dtypes.int64 }, cast_dtype_ => cast_dtype_ == cast_dtype))
                    return tensor.TensorShape.unknown_shape((int)shape.dims[0]);

                long[] x_ = { };
                foreach (var x in pre_cast.as_list())
                    if (x != -1)
                        x_[x_.Length] = x;
                    else
                        x_[x_.Length] = -1;
                var dest_dtype_shape_array = np.array(x_).astype(cast_dtype.as_system_dtype());

                long[] y_ = { };
                foreach (int y in dest_dtype_shape_array.Data<int>())
                    if (y >= 0)
                        y_[y_.Length] = y;
                    else
                        y_[y_.Length] = -1;
                return new TensorShape(y_);
            }
            else if (tensor.op.type == "Shape")
            {
                return tensor.op.inputs[0].shape;
            }
            else if (tensor.op.type == "Pack")
            {
                var ret_ = new TensorShape(new int[] { });
                if ((int)tensor.op.get_attr("axis") != 0)
                    throw new ValueError(String.Format(
                        @"Since rank 1 inputs are expected, Pack's axis: {0} must be 0, otherwise it
would not be rank 1.", tensor.op.get_attr("axis")));
                foreach (Tensor pack_input in tensor.op.inputs)
                {
                    var pack_input_val = constant_value(pack_input);
                    Dimension new_dim;
                    if (pack_input_val < 0)
                    {
                        new_dim = new Dimension(-1);
                    }
                    else if (pack_input_val == null)
                    {
                        new_dim = new Dimension(-1);
                    }
                    else
                    {
                        new_dim = new Dimension(pack_input_val);
                    }
                    ret_ = ret_.concatenate(new long[] { new_dim });
                }
                return ret_;
            }
            else if (tensor.op.type == "Concat")
            {
                var ret_ = new TensorShape(new int[] { });

                var inputlist_ = new ArraySegment<Tensor>(tensor.op.inputs, 1,
                                                        tensor.op.inputs.Length - 1);
                foreach (var concat_input in inputlist_)
                {
                    ret_ = ret_.concatenate(constant_value_as_shape(concat_input));
                }
                return ret_;
            }
            else if (tensor.op.type == "StridedSlice")
            {
                try
                {
                    var begin = constant_value(tensor.op.inputs[1]);
                    var end = constant_value(tensor.op.inputs[2]);
                    var strides = constant_value(tensor.op.inputs[3]);
                    if (new[] { begin, end, strides }.All(x => x == null))
                    {
                        begin = begin[0];
                        end = end[0];
                        strides = strides[0];
                        var begin_mask = tensor.op.get_attr("begin_mask");
                        if ((int)begin_mask == 1)
                        {
                            begin = null;
                        }
                        var end_mask = tensor.op.get_attr("end_mask");
                        if ((int)end_mask == 1)
                        {
                            end = null;
                        }

                        var ellipsis_mask = tensor.op.get_attr("ellipsis_mask");
                        var new_axis_mask = tensor.op.get_attr("new_axis_mask");
                        var shrink_axis_mask = tensor.op.get_attr("shrink_axis_mask");

                        bool valid_attributes;
                        if (!(bool)ellipsis_mask && !(bool)new_axis_mask &&
                            !(bool)shrink_axis_mask && !((bool)begin_mask || (int)begin_mask == 1) &&
                            !((bool)end_mask || (int)end_mask == 1))
                        {
                            valid_attributes = true;
                        }
                        else { valid_attributes = false; }
                        if (valid_attributes)
                        {
                            // sorry for the mess here, but this hacky solution was the best way
                            // i could come up with to implement the things done in python in c#
                            var prev_ = constant_value_as_shape(tensor.op.inputs[0]).dims;
                            var prev = prev_.Skip(begin).Take(end - begin).ToArray();
                            // 100 being the comparison doesn't really matter here; it's going to break anyway
                            for (int iter = 0; iter != 100; iter = iter + strides)
                            {
                                prev[prev.Length] = prev_[iter];
                                if ((iter + strides) > prev_.Length)
                                    break;
                            }
                            var ret_ = new TensorShape(prev);
                            return ret_;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex is ValueError || ex is TypeError) { }
                }
            }
            else if (tensor.op.type == "Placeholder" &&
                  tensor.op.graph.building_function &&
                  tensor.op.graph is FuncGraph func_graph)
            {
                int i = 0;
                foreach (Tensor capture in func_graph.internal_captures)
                {
                    if (capture.GetType() == typeof(Tensor))
                    {
                        var external_capture = func_graph.external_captures[i];
                        return constant_value_as_shape(external_capture);
                    }

                    i++;
                }
            }

            var ret = tensor.TensorShape.unknown_shape((int)shape.dims[0]);
            var value = constant_value(tensor);
            if (!(value is null))
            {
                var d_ = new int[value.size];
                foreach (var (index, d) in enumerate(value.ToArray<int>()))
                    d_[index] = d >= 0 ? d : -1;
                
                ret = ret.merge_with(new TensorShape(d_));
            }
            return ret;
        }

        public static TensorShapeProto as_shape<T>(T[] dims)
        {
            TensorShapeProto shape = new TensorShapeProto();

            for (int i = 0; i < dims.Length; i++)
            {
                var dim = new TensorShapeProto.Types.Dim();
                switch (dims[i])
                {
                    case int n:
                        dim.Size = n;
                        break;
                    case long l:
                        dim.Size = l;
                        break;
                    default:
                        throw new NotImplementedException("as_shape Not Implemented");
                }
                // dim.Name = $"dim_{i}";

                shape.Dim.Add(dim);
            }

            return shape;
        }

        public static TensorShape to_shape(long[] dims)
        {
            return new TensorShape(dims.Select(x => (int)x).ToArray());
        }

        public static TensorShape to_shape(int[] dims)
        {
            return new TensorShape(dims);
        }

        public static TensorShape as_shape(this Shape shape)
        {
            return new TensorShape(shape.dims);
        }

        public static TensorShape reshape(this Shape shape, int[] dims)
        {
            return new TensorShape(dims);
        }

        public static TensorShapeProto as_proto(this TensorShape tshape)
        {
            TensorShapeProto shape = new TensorShapeProto();

            for (int i = 0; i < tshape.ndim; i++)
            {
                var dim = new TensorShapeProto.Types.Dim();
                dim.Size = tshape.dims[i];
                //dim.Name = $"dim_{i}";

                shape.Dim.Add(dim);
            }

            return shape;
        }

        public static Tensor shape_tensor(int[] shape)
        {
            return ops.convert_to_tensor(shape, dtype: TF_DataType.TF_INT32, name: "shape");
        }

        public static string to_numpy_string(Tensor tensor)
        {
            var dtype = tensor.dtype;
            var shape = tensor.shape;

            if (dtype == TF_DataType.TF_STRING)
            {
                if (tensor.rank == 0)
                    return "'" + string.Join(string.Empty, tensor.StringBytes()[0]
                        .Take(25)
                        .Select(x => x < 32 || x > 127 ? "\\x" + x.ToString("x") : Convert.ToChar(x).ToString())) + "'";
                else
                    return $"['{string.Join("', '", tensor.StringData().Take(25))}']";
            }
            else if(dtype == TF_DataType.TF_VARIANT)
            {
                return "<unprintable>";
            }
            else if (dtype == TF_DataType.TF_RESOURCE)
            {
                return "<unprintable>";
            }
            else if (dtype == TF_DataType.TF_BOOL)
            {
                var array = tensor.ToArray<bool>();
                return DisplayArrayAsString(array, tensor.shape);
            }
            else if (dtype == TF_DataType.TF_INT32)
            {
                var array = tensor.ToArray<int>();
                return DisplayArrayAsString(array, tensor.shape);
            }
            else if (dtype == TF_DataType.TF_INT64)
            {
                var array = tensor.ToArray<long>();
                return DisplayArrayAsString(array, tensor.shape);
            }
            else if (dtype == TF_DataType.TF_FLOAT)
            {
                var array = tensor.ToArray<float>();
                return DisplayArrayAsString(array, tensor.shape);
            }
            else if(dtype == TF_DataType.TF_DOUBLE)
            {
                var array = tensor.ToArray<double>();
                return DisplayArrayAsString(array, tensor.shape);
            }
            else
            {
                var array = tensor.ToArray<byte>();
                return DisplayArrayAsString(array, tensor.shape);
            }
        }

        static string DisplayArrayAsString<T>(T[] array, Shape shape)
        {
            if (shape.ndim == 0)
                return array[0].ToString();

            var display = "[";
            if (array.Length < 10)
                display += string.Join(", ", array);
            else
                display += string.Join(", ", array.Take(3)) + " ... " + string.Join(", ", array.Skip(array.Length - 3));
            return display + "]";
        }
        

        public static ParsedSliceArgs ParseSlices(Slice[] slices)
        {
            var begin = new List<int>();
            var end = new List<int>();
            var strides = new List<int>();

            var index = 0;
            var (new_axis_mask, shrink_axis_mask) = (0, 0);
            var (begin_mask, end_mask) = (0, 0);
            var ellipsis_mask = 0;

            foreach (var s in slices)
            {
                if (s.IsNewAxis)
                {
                    begin.Add(0);
                    end.Add(0);
                    strides.Add(1);
                    new_axis_mask |= (1 << index);
                }
                else if (s.IsEllipsis)
                {
                    begin.Add(0);
                    end.Add(0);
                    strides.Add(1);
                    ellipsis_mask |= (1 << index);
                }
                else
                {
                    if (s.Start.HasValue)
                    {
                        begin.Add(s.Start.Value);
                    }
                    else
                    {
                        begin.Add(0);
                        begin_mask |= (1 << index);
                    }

                    if (s.Stop.HasValue)
                    {
                        end.Add(s.Stop.Value);
                    }
                    else
                    {
                        end.Add(0);
                        end_mask |= (1 << index);
                    }

                    strides.Add(s.Step);
                    if (s.IsIndex)
                        shrink_axis_mask |= (1 << index);
                }

                index += 1;
            }

            return new ParsedSliceArgs
            {
                Begin = begin.ToArray(),
                End = end.ToArray(),
                Strides = strides.ToArray(),
                BeginMask = begin_mask,
                EndMask = end_mask,
                EllipsisMask = ellipsis_mask,
                ShrinkAxisMask = shrink_axis_mask,
                NewAxisMask = new_axis_mask
            };
        }

        public static ParsedSliceArgs ParseSlices(Tensor start, Tensor stop = null, Tensor step = null)
        {
            var begin = new List<Tensor>();
            var end = new List<Tensor>();
            var strides = new List<Tensor>();

            var index = 0;
            var (new_axis_mask, shrink_axis_mask) = (0, 0);
            var (begin_mask, end_mask) = (0, 0);
            var ellipsis_mask = 0;

            begin.Add(start);

            if (stop == null)
                end.Add(start + 1);
            else
                end.Add(stop);

            shrink_axis_mask |= (1 << index);

            if (step == null)
                strides.Add(tf.constant(1, dtype: start.dtype));
            else
                strides.Add(step);

            return new ParsedSliceArgs
            {
                PackedBegin = array_ops.stack(begin),
                PackedEnd = array_ops.stack(end),
                PackedStrides = array_ops.stack(strides),
                BeginMask = begin_mask,
                EndMask = end_mask,
                EllipsisMask = ellipsis_mask,
                ShrinkAxisMask = shrink_axis_mask,
                NewAxisMask = new_axis_mask
            };
        }
    }
}
