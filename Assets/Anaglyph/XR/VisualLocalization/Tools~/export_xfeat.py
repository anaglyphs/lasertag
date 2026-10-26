"""
Exports XFeat (sparse extractor) and LighterGlue to Sentis-friendly ONNX.

  xfeat_dense.onnx     image [1,1,H,W] (0..1 grayscale)
                       -> score [1,1,H,W] (keypoint score, zero off NMS peaks), feats [1,64,H/8,W/8] (unit)
                       top-K and descriptor sampling happen on the CPU: Unity Inference
                       Engine's GPU TopK over H*W took ~200 ms, the dense net ~5 ms
  lighterglue.onnx     kpts0 [N0,2], desc0 [N0,64], kpts1 [N1,2], desc1 [N1,64] (pixels at H,W)
                       -> matches0 [N0] (index into 1, or -1), mscores0 [N0]

Everything static except the matcher's point counts; no NonZero, no bicubic, no
unfold, no SDPA, so the graph stays within what Unity Inference Engine imports.

Usage (XFeat is Apache-2.0: https://github.com/verlab/accelerated_features):
  git clone --depth 1 https://github.com/verlab/accelerated_features
  pip install torch kornia onnx onnxruntime opencv-python-headless tqdm
  XFEAT_ROOT=accelerated_features python export_xfeat.py ../Resources/XFeat
"""
import sys, os
import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F

ROOT = os.environ.get("XFEAT_ROOT", os.path.join(os.path.dirname(__file__), "ext", "accelerated_features"))
sys.path.insert(0, ROOT)
from modules.model import XFeatModel  # noqa: E402

# XFeat's input size, any multiple of 32 (XFEAT_SIZE="WxH"); XFeatExtractor.Width/Height
# must match. LighterGlue normalizes keypoint positions for the same image size
W, H = map(int, os.environ.get("XFEAT_SIZE", "544x544").lower().split("x"))
GLUE_W, GLUE_H = W, H
K = 1024
DETECTION_THRESHOLD = 0.05
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "onnx")
os.makedirs(OUT, exist_ok=True)


class XFeatExtract(nn.Module):
    def __init__(self):
        super().__init__()
        self.net = XFeatModel()
        self.net.load_state_dict(torch.load(os.path.join(ROOT, "weights", "xfeat.pt"), map_location="cpu"))
        self.net.eval()

    def backbone(self, x):
        n = self.net
        x = n.norm(x)
        x1 = n.block1(x)
        x2 = n.block2(x1 + n.skip1(x))
        x3 = n.block3(x2)
        x4 = n.block4(x3)
        x5 = n.block5(x4)
        x4 = F.interpolate(x4, (x3.shape[-2], x3.shape[-1]), mode="bilinear")
        x5 = F.interpolate(x5, (x3.shape[-2], x3.shape[-1]), mode="bilinear")
        feats = n.block_fusion(x3 + x4 + x5)
        heat = n.heatmap_head(feats)
        # unfold2d(ws=8) for one channel, as a reshape: channel = dy*8 + dx
        cells = x.reshape(1, 1, H // 8, 8, W // 8, 8).permute(0, 1, 3, 5, 2, 4).reshape(1, 64, H // 8, W // 8)
        logits = n.keypoint_head(cells)
        return feats, logits, heat

    def forward(self, image):
        feats, logits, heat = self.backbone(image)
        feats = F.normalize(feats, dim=1)

        # keypoint heatmap: softmax over 65, drop the dustbin, pixel-shuffle to full res
        prob = F.softmax(logits, 1)[:, :64]
        kh = prob.reshape(1, 8, 8, H // 8, W // 8).permute(0, 3, 1, 4, 2).reshape(1, 1, H, W)

        # 5x5 NMS above threshold, scored by the reliability map
        local_max = F.max_pool2d(kh, 5, stride=1, padding=2)
        keep = (kh == local_max) & (kh > DETECTION_THRESHOLD)
        rel = F.interpolate(heat, (H, W), mode="bilinear", align_corners=False)
        score = torch.where(keep, kh * rel, torch.zeros_like(kh))
        return score, feats


def sparse(score, feats):
    """What XFeatExtractor does on the CPU: top-K peaks, bilinear descriptors in XFeat's grid normalization."""
    s = score.reshape(-1)
    idx = np.argsort(-s, kind="stable")[:K]
    idx = idx[s[idx] > 0]
    x, y = (idx % W).astype(np.float32), (idx // W).astype(np.float32)
    f = torch.from_numpy(feats)
    grid = torch.from_numpy(np.stack([2 * x / (W - 1) - 1, 2 * y / (H - 1) - 1], -1)).reshape(1, -1, 1, 2)
    desc = F.grid_sample(f, grid, mode="bilinear", align_corners=False).reshape(64, -1).T
    return np.stack([x, y], -1), s[idx], F.normalize(desc, dim=-1).numpy()


def rotate_half(x):
    x = x.reshape(-1, 48, 2)
    return torch.stack((-x[..., 1], x[..., 0]), -1).reshape(-1, 96)


class LighterGlueExport(nn.Module):
    """kornia LightGlue (xfeat conf: 6 layers, 1 head, dim 96), unbatched, no pruning or early exit."""

    def __init__(self):
        super().__init__()
        from kornia.feature.lightglue import LightGlue
        conf = dict(name="xfeat", input_dim=64, descriptor_dim=96, add_scale_ori=False, add_laf=False,
                    scale_coef=1.0, n_layers=6, num_heads=1, flash=False, mp=False,
                    depth_confidence=-1, width_confidence=-1, filter_threshold=0.1, weights=None)
        LightGlue.default_conf = conf
        lg = LightGlue(None)
        sd = torch.load(os.path.join(ROOT, "weights", "xfeat-lighterglue.pt"), map_location="cpu")
        sd = {k[len("matcher."):] if k.startswith("matcher.") else k: v for k, v in sd.items()}
        for i in range(6):
            sd = {k.replace(f"self_attn.{i}", f"transformers.{i}.self_attn"): v for k, v in sd.items()}
            sd = {k.replace(f"cross_attn.{i}", f"transformers.{i}.cross_attn"): v for k, v in sd.items()}
        missing, unexpected = lg.load_state_dict(sd, strict=False)
        missing = [m for m in missing if not m.startswith("confidence_thresholds")]
        assert not missing, f"missing weights: {missing}"
        self.lg = lg.eval()
        self.min_conf = 0.1

    def posenc(self, kpts):
        proj = self.lg.posenc.Wr(kpts)  # N, 48
        # repeat_interleave(2) as stack + reshape: the legacy exporter gets it wrong for dynamic N
        c, s = torch.cos(proj), torch.sin(proj)
        cos = torch.stack([c, c], -1).reshape(-1, 96)
        sin = torch.stack([s, s], -1).reshape(-1, 96)
        return cos, sin

    @staticmethod
    def attend(q, k, v):
        s = q.shape[-1] ** -0.5
        return torch.softmax(q @ k.transpose(-1, -2) * s, -1) @ v

    def self_block(self, blk, x, cos, sin):
        qkv = blk.Wqkv(x).reshape(-1, 96, 3)  # one head: N, 96, 3
        q, k, v = qkv[..., 0], qkv[..., 1], qkv[..., 2]
        q = q * cos + rotate_half(q) * sin
        k = k * cos + rotate_half(k) * sin
        msg = blk.out_proj(self.attend(q, k, v))
        return x + blk.ffn(torch.cat([x, msg], -1))

    def cross_block(self, blk, x0, x1):
        s = blk.scale ** 0.5
        qk0, qk1 = blk.to_qk(x0) * s, blk.to_qk(x1) * s
        v0, v1 = blk.to_v(x0), blk.to_v(x1)
        sim = qk0 @ qk1.transpose(0, 1)
        m0 = torch.softmax(sim, -1) @ v1
        m1 = torch.softmax(sim.transpose(0, 1), -1) @ v0
        m0, m1 = blk.to_out(m0), blk.to_out(m1)
        return x0 + blk.ffn(torch.cat([x0, m0], -1)), x1 + blk.ffn(torch.cat([x1, m1], -1))

    def forward(self, kpts0, desc0, kpts1, desc1):
        shift = torch.tensor([GLUE_W / 2, GLUE_H / 2])
        scale = max(GLUE_W, GLUE_H) / 2
        k0, k1 = (kpts0 - shift) / scale, (kpts1 - shift) / scale
        cos0, sin0 = self.posenc(k0)
        cos1, sin1 = self.posenc(k1)
        d0, d1 = self.lg.input_proj(desc0), self.lg.input_proj(desc1)
        for layer in self.lg.transformers:
            d0 = self.self_block(layer.self_attn, d0, cos0, sin0)
            d1 = self.self_block(layer.self_attn, d1, cos1, sin1)
            d0, d1 = self.cross_block(layer.cross_attn, d0, d1)

        la = self.lg.log_assignment[-1]
        md0, md1 = la.final_proj(d0), la.final_proj(d1)
        dim = md0.shape[-1]
        md0, md1 = md0 / dim ** 0.25, md1 / dim ** 0.25
        sim = md0 @ md1.transpose(0, 1)
        z0, z1 = la.matchability(d0), la.matchability(d1)
        cert = F.logsigmoid(z0) + F.logsigmoid(z1).transpose(0, 1)
        scores = F.log_softmax(sim, 1) + F.log_softmax(sim, 0) + cert  # N0, N1

        best0, m0 = scores.max(1)
        _, m1 = scores.max(0)
        idx0 = torch.arange(m0.shape[0])
        mutual = torch.gather(m1, 0, m0) == idx0
        conf = torch.exp(best0)
        valid = mutual & (conf > self.min_conf)
        matches0 = torch.where(valid, m0, torch.full_like(m0, -1))
        mscores0 = torch.where(valid, conf, torch.zeros_like(conf))
        return matches0.float(), mscores0


def export():
    ext = XFeatExtract().eval()
    img = torch.rand(1, 1, H, W)
    with torch.no_grad():
        torch.onnx.export(ext, (img,), os.path.join(OUT, "xfeat_dense.onnx"), opset_version=17,
                          input_names=["image"], output_names=["score", "feats"], dynamo=False)

    lg = LighterGlueExport().eval()
    args = (torch.rand(700, 2) * torch.tensor([GLUE_W, GLUE_H]), F.normalize(torch.randn(700, 64), dim=-1),
            torch.rand(900, 2) * torch.tensor([GLUE_W, GLUE_H]), F.normalize(torch.randn(900, 64), dim=-1))
    with torch.no_grad():
        torch.onnx.export(lg, args, os.path.join(OUT, "lighterglue.onnx"), opset_version=17,
                          input_names=["kpts0", "desc0", "kpts1", "desc1"], output_names=["matches0", "mscores0"],
                          dynamic_axes={"kpts0": {0: "n0"}, "desc0": {0: "n0"}, "kpts1": {0: "n1"}, "desc1": {0: "n1"}},
                          dynamo=False)
    return ext, lg


def validate(ext, lg):
    """Two real photos of the same scene: exported graphs vs the reference implementation."""
    import cv2
    import onnxruntime as ort
    from modules.xfeat import XFeat

    a = cv2.imread(os.path.join(ROOT, "assets", "ref.png"), cv2.IMREAD_GRAYSCALE)
    b = cv2.imread(os.path.join(ROOT, "assets", "tgt.png"), cv2.IMREAD_GRAYSCALE)
    a, b = (cv2.resize(i, (W, H)).astype(np.float32)[None, None] / 255 for i in (a, b))

    se = ort.InferenceSession(os.path.join(OUT, "xfeat_dense.onnx"))
    sm = ort.InferenceSession(os.path.join(OUT, "lighterglue.onnx"))
    ka, sa, da = sparse(*se.run(None, {"image": a}))
    kb, sb, db = sparse(*se.run(None, {"image": b}))
    va, vb = sa > 0, sb > 0
    print(f"keypoints: {va.sum()} / {vb.sum()} valid of {K}")

    ref = XFeat(top_k=K)
    ra = ref.detectAndCompute(torch.from_numpy(a), top_k=K)[0]
    overlap = len(set(map(tuple, ra["keypoints"].round().int().tolist())) & set(map(tuple, ka[va].astype(int).tolist())))
    print(f"reference keypoints shared: {overlap} / {len(ra['keypoints'])}")

    m0, ms = sm.run(None, {"kpts0": ka[va], "desc0": da[va], "kpts1": kb[vb], "desc1": db[vb]})
    good = m0 >= 0
    print(f"exported matcher: {good.sum()} matches")
    p0 = ka[va][good]
    p1 = kb[vb][m0[good].astype(int)]
    if good.sum() >= 8:
        _, inl = cv2.findHomography(p0, p1, cv2.USAC_MAGSAC, 4.0)
        print(f"homography inliers: {int(inl.sum())} / {good.sum()}")

    with torch.no_grad():
        t0, _ = lg(torch.from_numpy(ka[va]), torch.from_numpy(da[va]), torch.from_numpy(kb[vb]), torch.from_numpy(db[vb]))
    print(f"torch wrapper agrees with onnx on {(t0.numpy() == m0).mean() * 100:.1f}% of matches")


if __name__ == "__main__":
    ext, lg = export()
    validate(ext, lg)
