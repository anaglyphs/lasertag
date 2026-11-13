"""Rebuilds the skysegsmall ncnn network (U2NetP trained on sky) as ONNX.

Source: https://github.com/xiongzhu666/Sky-Segmentation-and-Post-processing (MIT)
  skysegsmall_sim-opt-fp16.param / .bin

The ONNX takes "image" [1, 1, SIZE, SIZE] grayscale in 0..1, row 0 on top, and returns
"sky" [1, 1, SIZE, SIZE], the probability each pixel is sky. The network was trained on
color; gray on all three channels scores the same against the repo's masks (IoU 0.83 at
192, 0.80 at 128), and the normalization is inside the graph.

  python export_sky.py <param> <bin> <out.onnx> [SIZE=128, a multiple of 32]
"""
import sys

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

NATIVE_SIZE = 384
FP16_FLAG = 0x01306B47


def parse_layers(param_path):
    lines = open(param_path).read().split("\n")
    layers = []
    for line in lines[2:]:
        t = line.split()
        if not t:
            continue
        kind, name, n_in, n_out = t[0], t[1], int(t[2]), int(t[3])
        inputs = t[4:4 + n_in]
        outputs = t[4 + n_in:4 + n_in + n_out]
        params = {int(k): v for k, v in (p.split("=") for p in t[4 + n_in + n_out:])}
        layers.append((kind, name, inputs, outputs, params))
    return layers


class Weights:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        self.at = 0

    def tagged(self, count):
        flag = int.from_bytes(self.data[self.at:self.at + 4], "little")
        self.at += 4
        if flag == FP16_FLAG:
            values = np.frombuffer(self.data, np.float16, count, self.at).astype(np.float32)
            self.at += (count * 2 + 3) // 4 * 4
        elif flag == 0:
            values = self.raw(count)
        else:
            raise ValueError(f"unsupported weight storage flag {flag:#x}")
        return values

    def raw(self, count):
        values = np.frombuffer(self.data, np.float32, count, self.at).copy()
        self.at += count * 4
        return values


def export(param_path, bin_path, out_path, size):
    layers = parse_layers(param_path)
    weights = Weights(bin_path)
    scale = size / NATIVE_SIZE
    nodes, initializers = [], []
    alias = {}  # ncnn Split copies -> the one ONNX tensor
    shapes = {}
    mult_adds = 0

    def src(name):
        return alias.get(name, name)

    def const(name, array):
        initializers.append(numpy_helper.from_array(array, name))
        return name

    # gray broadcast over the three channels, ImageNet-standardized as the network was trained
    mean = np.array([0.485, 0.456, 0.406], np.float32).reshape(1, 3, 1, 1)
    std = np.array([0.229, 0.224, 0.225], np.float32).reshape(1, 3, 1, 1)
    nodes.append(helper.make_node("Sub", ["image", const("mean", mean)], ["centered"]))

    final_output = None
    for kind, name, inputs, outputs, p in layers:
        if kind == "Input":
            nodes.append(helper.make_node("Div", ["centered", const("std", std)], [outputs[0]]))
            shapes[outputs[0]] = (3, size, size)
        elif kind == "Split":
            for o in outputs:
                alias[o] = src(inputs[0])
        elif kind == "Convolution":
            out_c, k = int(p[0]), int(p[1])
            dilation, pad = int(p.get(2, 1)), int(p.get(4, 0))
            count = int(p[6])
            in_c = count // (out_c * k * k)
            w = weights.tagged(count).reshape(out_c, in_c, k, k)
            b = weights.raw(out_c) if int(p.get(5, 0)) else np.zeros(out_c, np.float32)
            activation = int(p.get(9, 0))
            conv_out = outputs[0] if activation == 0 else name + "_pre"
            nodes.append(helper.make_node(
                "Conv", [src(inputs[0]), const(name + "_w", w), const(name + "_b", b)], [conv_out],
                kernel_shape=[k, k], pads=[pad] * 4, dilations=[dilation] * 2, name=name))
            if activation == 1:
                nodes.append(helper.make_node("Relu", [conv_out], [outputs[0]]))
            elif activation == 4:
                nodes.append(helper.make_node("Sigmoid", [conv_out], [outputs[0]]))
                final_output = outputs[0]
            elif activation != 0:
                raise ValueError(f"unsupported activation {activation}")
            _, h, wd = shapes[src(inputs[0])]
            assert shapes[src(inputs[0])][0] == in_c, name
            shapes[outputs[0]] = (out_c, h, wd)
            mult_adds += count * h * wd
        elif kind == "Pooling":
            assert int(p.get(0, 0)) == 0 and int(p[1]) == 2 and int(p[2]) == 2
            c, h, wd = shapes[src(inputs[0])]
            assert h % 2 == 0 and wd % 2 == 0, f"{name}: SIZE must be a multiple of 32"
            nodes.append(helper.make_node("MaxPool", [src(inputs[0])], [outputs[0]],
                                          kernel_shape=[2, 2], strides=[2, 2], name=name))
            shapes[outputs[0]] = (c, h // 2, wd // 2)
        elif kind == "Interp":
            assert int(p[0]) == 2 and int(p.get(6, 0)) == 0
            c, h, wd = shapes[src(inputs[0])]
            oh, ow = round(int(p[3]) * scale), round(int(p[4]) * scale)
            if (oh, ow) == (h, wd):
                alias[outputs[0]] = src(inputs[0])
            else:
                sizes = const(name + "_sizes", np.array([1, c, oh, ow], np.int64))
                nodes.append(helper.make_node(
                    "Resize", [src(inputs[0]), "", "", sizes], [outputs[0]],
                    mode="linear", coordinate_transformation_mode="half_pixel", name=name))
                shapes[outputs[0]] = (c, oh, ow)
        elif kind == "Concat":
            assert int(p.get(0, 0)) == 0
            ins = [src(i) for i in inputs]
            nodes.append(helper.make_node("Concat", ins, [outputs[0]], axis=1, name=name))
            _, h, wd = shapes[ins[0]]
            shapes[outputs[0]] = (sum(shapes[i][0] for i in ins), h, wd)
        elif kind == "BinaryOp":
            assert int(p.get(0, 0)) == 0
            nodes.append(helper.make_node("Add", [src(inputs[0]), src(inputs[1])], [outputs[0]], name=name))
            shapes[outputs[0]] = shapes[src(inputs[0])]
        elif kind == "Sigmoid":
            pass  # the side outputs' own masks, which nothing reads
        else:
            raise ValueError(f"unsupported layer {kind}")

    assert weights.at == len(weights.data), "weights left over: the graph was misread"
    nodes.append(helper.make_node("Identity", [final_output], ["sky"]))

    graph = helper.make_graph(
        nodes, "skysegsmall",
        [helper.make_tensor_value_info("image", TensorProto.FLOAT, [1, 1, size, size])],
        [helper.make_tensor_value_info("sky", TensorProto.FLOAT, [1, 1, size, size])],
        initializers)
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
    model.ir_version = 8
    onnx.checker.check_model(model)
    onnx.save(model, out_path)
    print(f"{out_path}: {size}x{size}, {len(nodes)} nodes, {mult_adds / 1e9:.2f} G multiply-adds")


if __name__ == "__main__":
    export(sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 128)
