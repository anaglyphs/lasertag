"""Rebuilds the EGE-UNet sky network (ncnn, exported through pnnx) as ONNX.

Source: https://github.com/xiongzhu666/Sky-Segmentation-and-Post-processing (MIT)
  EGE_165.ncnn.param / .bin

The ONNX takes "image" [1, 1, SIZE, SIZE] grayscale in 0..1, row 0 on top, and returns
"sky" [1, 1, SIZE, SIZE], the probability each pixel is sky. The network was trained on
color stretched to its own min..max; gray on all three channels is folded into the first
convolution. Its attention tables are stored at the 384 size's feature maps and are
resampled for any other SIZE.

  python export_sky_ege.py <param> <bin> <out.onnx> [SIZE=384, a multiple of 32]
"""
import sys

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

NATIVE_SIZE = 384
FP16_FLAG = 0x01306B47

# ncnn's Permute order on (c, h, w) blobs, as an ONNX perm of [n, c, h, w]
PERMUTES = {0: [0, 1, 2, 3], 1: [0, 1, 3, 2], 2: [0, 2, 1, 3], 3: [0, 2, 3, 1], 4: [0, 3, 1, 2], 5: [0, 3, 2, 1]}


def parse_layers(param_path):
    layers = []
    for line in open(param_path).read().split("\n")[2:]:
        t = line.split()
        if not t:
            continue
        n_in, n_out = int(t[2]), int(t[3])
        params = dict(p.split("=") for p in t[4 + n_in + n_out:])
        layers.append((t[0], t[1], t[4:4 + n_in], t[4 + n_in:4 + n_in + n_out], params))
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
            return values
        if flag != 0:
            raise ValueError(f"unsupported weight storage flag {flag:#x}")
        return self.raw(count)

    def raw(self, count):
        values = np.frombuffer(self.data, np.float32, count, self.at).copy()
        self.at += count * 4
        return values


def resample_axis(table, axis, new_length):
    """Linear resampling with the ends kept (align_corners), as the tables were first stretched."""
    old_length = table.shape[axis]
    if old_length == new_length:
        return table
    at = np.linspace(0, old_length - 1, new_length)
    low = np.floor(at).astype(int).clip(0, old_length - 2)
    t = (at - low).astype(np.float32)
    shape = [1] * table.ndim
    shape[axis] = new_length
    return np.take(table, low, axis) * (1 - t.reshape(shape)) + np.take(table, low + 1, axis) * t.reshape(shape)


def export(param_path, bin_path, out_path, size):
    layers = parse_layers(param_path)
    weights = Weights(bin_path)
    nodes, initializers, shapes, alias = [], [], {}, {}
    tables = {}
    mult_adds = 0

    def src(name):
        return alias.get(name, name)

    def const(name, array):
        initializers.append(numpy_helper.from_array(np.ascontiguousarray(array), name))
        return name

    def add(op, inputs, outputs, **attributes):
        nodes.append(helper.make_node(op, inputs, outputs, **attributes))

    # the image stretched to its own min..max, as the network was trained (0..255, folded
    # into the first convolution)
    add("ReduceMin", ["image"], ["image_min"], keepdims=1)
    add("ReduceMax", ["image"], ["image_max"], keepdims=1)
    add("Sub", ["image", "image_min"], ["image_from_zero"])
    add("Sub", ["image_max", "image_min"], ["image_range"])
    add("Add", ["image_range", const("tiny", np.array(1e-6, np.float32))], ["image_range_safe"])

    first_convolution = True
    for kind, name, inputs, outputs, p in layers:
        ins = [src(i) for i in inputs]
        out = outputs[0] if outputs else None
        if kind == "Input":
            add("Div", ["image_from_zero", "image_range_safe"], [out])
            shapes[out] = (1, size, size)
        elif kind == "Split":
            for o in outputs:
                alias[o] = ins[0]
        elif kind in ("Convolution", "ConvolutionDepthWise"):
            out_c, kw, kh = int(p["0"]), int(p["1"]), int(p.get("11", p["1"]))
            dw, dh = int(p.get("2", 1)), int(p.get("12", p.get("2", 1)))
            assert int(p.get("3", 1)) == 1 and int(p.get("13", 1)) == 1, name
            pw, ph = int(p.get("4", 0)), int(p.get("14", p.get("4", 0)))
            group = int(p.get("7", 1))
            count = int(p["6"])
            in_c = count // (out_c * kw * kh) * group
            w = weights.tagged(count).reshape(out_c, in_c // group, kh, kw)
            b = weights.raw(out_c) if int(p.get("5", 0)) else np.zeros(out_c, np.float32)
            if first_convolution:
                # gray on all three channels, scaled to 0..255
                w = w.sum(axis=1, keepdims=True) * 255
                in_c = 1
                first_convolution = False
            c, h, wd = shapes[ins[0]]
            assert c == in_c, f"{name}: {c} channels in, weights for {in_c}"
            add("Conv", [ins[0], const(name + "_w", w), const(name + "_b", b)], [out],
                kernel_shape=[kh, kw], pads=[ph, pw, ph, pw], dilations=[dh, dw], group=group, name=name)
            oh, ow = h + 2 * ph - dh * (kh - 1), wd + 2 * pw - dw * (kw - 1)
            shapes[out] = (out_c, oh, ow)
            mult_adds += count * oh * ow
        elif kind == "GroupNorm":
            groups, channels, eps = int(p["0"]), int(p["1"]), float(p["2"])
            gamma, beta = weights.raw(channels), weights.raw(channels)
            c, h, wd = shapes[ins[0]]
            add("Reshape", [ins[0], const(name + "_grouped", np.array([1, groups, c // groups * h, wd], np.int64))], [name + "_g"])
            add("InstanceNormalization", [name + "_g", const(name + "_one", np.ones(groups, np.float32)),
                                          const(name + "_zero", np.zeros(groups, np.float32))], [name + "_n"], epsilon=eps)
            add("Reshape", [name + "_n", const(name + "_shape", np.array([1, c, h, wd], np.int64))], [name + "_r"])
            add("Mul", [name + "_r", const(name + "_gamma", gamma.reshape(1, c, 1, 1))], [name + "_s"])
            add("Add", [name + "_s", const(name + "_beta", beta.reshape(1, c, 1, 1))], [out])
            shapes[out] = shapes[ins[0]]
        elif kind == "LayerNorm":
            # over the last axis: the channels, which the Permutes around it put there
            length, eps = int(p["0"]), float(p["1"])
            gamma, beta = weights.raw(length), weights.raw(length)
            assert shapes[ins[0]][2] == length, name
            add("ReduceMean", [ins[0]], [name + "_mean"], axes=[-1], keepdims=1)
            add("Sub", [ins[0], name + "_mean"], [name + "_d"])
            add("Mul", [name + "_d", name + "_d"], [name + "_dd"])
            add("ReduceMean", [name + "_dd"], [name + "_var"], axes=[-1], keepdims=1)
            add("Add", [name + "_var", const(name + "_eps", np.array(eps, np.float32))], [name + "_ve"])
            add("Sqrt", [name + "_ve"], [name + "_std"])
            add("Div", [name + "_d", name + "_std"], [name + "_n"])
            add("Mul", [name + "_n", const(name + "_gamma", gamma)], [name + "_s"])
            add("Add", [name + "_s", const(name + "_beta", beta)], [out])
            shapes[out] = shapes[ins[0]]
        elif kind == "GELU":
            add("Div", [ins[0], const(name + "_sqrt2", np.array(np.sqrt(2), np.float32))], [name + "_x"])
            add("Erf", [name + "_x"], [name + "_erf"])
            add("Add", [name + "_erf", const(name + "_one", np.array(1, np.float32))], [name + "_e1"])
            add("Mul", [ins[0], name + "_e1"], [name + "_xe"])
            add("Mul", [name + "_xe", const(name + "_half", np.array(0.5, np.float32))], [out])
            shapes[out] = shapes[ins[0]]
        elif kind == "Pooling":
            assert p["0"] == "0" and p["1"] == "2" and p["2"] == "2", name
            c, h, wd = shapes[ins[0]]
            assert h % 2 == 0 and wd % 2 == 0, f"{name}: SIZE must be a multiple of 32"
            add("MaxPool", [ins[0]], [out], kernel_shape=[2, 2], strides=[2, 2], name=name)
            shapes[out] = (c, h // 2, wd // 2)
        elif kind == "Permute":
            perm = PERMUTES[int(p.get("0", 0))]
            add("Transpose", [ins[0]], [out], perm=perm)
            s = (1,) + shapes[ins[0]]
            shapes[out] = tuple(s[i] for i in perm[1:])
        elif kind == "Slice":
            assert p["1"] == "0", name
            c, h, wd = shapes[ins[0]]
            add("Split", [ins[0]], outputs, axis=1)
            for o in outputs:
                shapes[o] = (c // len(outputs), h, wd)
        elif kind == "MemoryData":
            wd, h, c = int(p["0"]), int(p.get("1", 1)), int(p.get("2", 1))
            tables[out] = weights.raw(wd * h * c).reshape(1, c, h, wd)
        elif kind == "BinaryOp":
            op = {"0": "Add", "2": "Mul"}[p.get("0", "0")]
            # an attention table, stored at the 384 size's feature map: resampled to the
            # feature map it multiplies here
            for table_name, other in ((ins[0], ins[1]), (ins[1], ins[0])):
                if table_name not in tables:
                    continue
                table = tables.pop(table_name)
                for axis in (1, 2, 3):
                    if table.shape[axis] != 1:
                        table = resample_axis(table, axis, shapes[other][axis - 1])
                const(table_name, table.astype(np.float32))
                shapes[table_name] = table.shape[1:]
            add(op, ins, [out], name=name)
            shapes[out] = tuple(np.broadcast_shapes(shapes[ins[0]], shapes[ins[1]]))
        elif kind == "Concat":
            assert p.get("0", "0") == "0", name
            add("Concat", ins, [out], axis=1, name=name)
            shapes[out] = (sum(shapes[i][0] for i in ins),) + shapes[ins[0]][1:]
        elif kind == "Interp":
            assert p["0"] == "2" and p.get("6", "0") == "1", name
            c, h, wd = shapes[ins[0]]
            if "3" in p:
                oh, ow = int(p["3"]) * size // NATIVE_SIZE, int(p["4"]) * size // NATIVE_SIZE
            else:
                oh, ow = round(h * float(p["1"])), round(wd * float(p["2"]))
            add("Resize", [ins[0], "", "", const(name + "_sizes", np.array([1, c, oh, ow], np.int64))], [out],
                mode="linear", coordinate_transformation_mode="align_corners", name=name)
            shapes[out] = (c, oh, ow)
        elif kind == "Sigmoid":
            add("Sigmoid", ins, [out])
            shapes[out] = shapes[ins[0]]
        else:
            raise ValueError(f"unsupported layer {kind}")

    assert weights.at == len(weights.data), "weights left over: the graph was misread"
    assert not tables, "an attention table feeds something other than a product"
    add("Identity", ["out5"], ["sky"])

    # the side outputs' branches feed nothing
    needed, kept = {"sky"}, []
    for node in reversed(nodes):
        if any(o in needed for o in node.output):
            kept.append(node)
            needed.update(i for i in node.input if i)
    kept.reverse()
    initializers = [i for i in initializers if i.name in needed]

    graph = helper.make_graph(
        kept, "sky_ege",
        [helper.make_tensor_value_info("image", TensorProto.FLOAT, [1, 1, size, size])],
        [helper.make_tensor_value_info("sky", TensorProto.FLOAT, [1, 1, size, size])],
        initializers)
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
    model.ir_version = 8
    onnx.checker.check_model(model)
    onnx.save(model, out_path)
    print(f"{out_path}: {size}x{size}, {len(kept)} nodes, {mult_adds / 1e9:.3f} G multiply-adds (side branches included)")


if __name__ == "__main__":
    export(sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else NATIVE_SIZE)
