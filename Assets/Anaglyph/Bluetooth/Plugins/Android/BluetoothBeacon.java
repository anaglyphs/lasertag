package com.anaglyph.bluetooth;

import android.annotation.SuppressLint;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothManager;
import android.bluetooth.le.AdvertiseCallback;
import android.bluetooth.le.AdvertiseData;
import android.bluetooth.le.AdvertiseSettings;
import android.bluetooth.le.BluetoothLeAdvertiser;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanFilter;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.os.ParcelUuid;
import android.util.Log;

import java.util.Collections;
import java.util.Iterator;
import java.util.LinkedHashSet;
import java.util.List;

/** Advertises one 128-bit BLE service UUID and collects the service UUIDs of nearby advertisers that start with a prefix. */
@SuppressLint("MissingPermission")
public final class BluetoothBeacon {
    private static final String TAG = "AnaglyphBluetoothBeacon";
    private static final int UUID_HEX_DIGITS = 32;

    private final Context context;
    private final LinkedHashSet<String> unreadSightings = new LinkedHashSet<>();

    private BluetoothLeAdvertiser advertiser;
    private AdvertiseCallback advertiseCallback;
    private String advertisedServiceUuid;

    private BluetoothLeScanner scanner;
    private ScanCallback scanCallback;
    private String scannedPrefixHex;

    public BluetoothBeacon(Context context) {
        this.context = context;
    }

    /** True while the advertisement for this UUID is running or starting. Safe to repeat. */
    public synchronized boolean startAdvertising(String serviceUuid) {
        BluetoothAdapter adapter = enabledAdapter();
        if (adapter == null) {
            forgetAdvertising();
            return false;
        }
        if (advertiser != null && serviceUuid.equals(advertisedServiceUuid)) return true;

        stopAdvertising();
        try {
            BluetoothLeAdvertiser newAdvertiser = adapter.getBluetoothLeAdvertiser();
            if (newAdvertiser == null) return false;

            AdvertiseSettings settings = new AdvertiseSettings.Builder()
                    .setAdvertiseMode(AdvertiseSettings.ADVERTISE_MODE_LOW_LATENCY)
                    .setTxPowerLevel(AdvertiseSettings.ADVERTISE_TX_POWER_HIGH)
                    .setConnectable(false)
                    .build();
            AdvertiseData data = new AdvertiseData.Builder()
                    .addServiceUuid(ParcelUuid.fromString(serviceUuid))
                    .setIncludeDeviceName(false)
                    .build();

            AdvertiseCallback newCallback = new AdvertiseCallback() {
                @Override
                public void onStartFailure(int errorCode) {
                    Log.w(TAG, "advertising failed with code " + errorCode);
                    synchronized (BluetoothBeacon.this) {
                        if (advertiseCallback == this) forgetAdvertising();
                    }
                }
            };

            newAdvertiser.startAdvertising(settings, data, newCallback);
            advertiser = newAdvertiser;
            advertiseCallback = newCallback;
            advertisedServiceUuid = serviceUuid;
            return true;
        } catch (RuntimeException exception) {
            Log.w(TAG, "advertising threw " + exception);
            return false;
        }
    }

    public synchronized void stopAdvertising() {
        if (advertiser == null) return;
        try {
            advertiser.stopAdvertising(advertiseCallback);
        } catch (RuntimeException exception) {
            Log.w(TAG, "stopping the advertisement threw " + exception);
        }
        forgetAdvertising();
    }

    /** True while the scan for this prefix is running. Safe to repeat. The prefix is the UUID's leading characters. */
    public synchronized boolean startScanning(String serviceUuidPrefix) {
        BluetoothAdapter adapter = enabledAdapter();
        if (adapter == null) {
            forgetScanning();
            return false;
        }
        String prefixHex = hexDigits(serviceUuidPrefix);
        if (scanner != null && prefixHex.equals(scannedPrefixHex)) return true;

        stopScanning();
        try {
            BluetoothLeScanner newScanner = adapter.getBluetoothLeScanner();
            if (newScanner == null) return false;

            ScanFilter prefixOnly = new ScanFilter.Builder()
                    .setServiceUuid(uuidFromHex(padded(prefixHex, '0')), uuidFromHex(padded(repeated('f', prefixHex.length()), '0')))
                    .build();
            ScanSettings settings = new ScanSettings.Builder()
                    .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
                    .build();

            ScanCallback newCallback = new ScanCallback() {
                @Override
                public void onScanResult(int callbackType, ScanResult result) {
                    if (result.getScanRecord() == null) return;
                    List<ParcelUuid> serviceUuids = result.getScanRecord().getServiceUuids();
                    if (serviceUuids == null) return;

                    synchronized (BluetoothBeacon.this) {
                        if (scanCallback != this) return;
                        for (ParcelUuid serviceUuid : serviceUuids) {
                            String uuid = serviceUuid.toString().toLowerCase();
                            if (hexDigits(uuid).startsWith(scannedPrefixHex)) unreadSightings.add(uuid);
                        }
                    }
                }

                @Override
                public void onScanFailed(int errorCode) {
                    Log.w(TAG, "scan failed with code " + errorCode);
                    synchronized (BluetoothBeacon.this) {
                        if (scanCallback == this) forgetScanning();
                    }
                }
            };

            newScanner.startScan(Collections.singletonList(prefixOnly), settings, newCallback);
            scanner = newScanner;
            scanCallback = newCallback;
            scannedPrefixHex = prefixHex;
            return true;
        } catch (RuntimeException exception) {
            Log.w(TAG, "scan threw " + exception);
            return false;
        }
    }

    public synchronized void stopScanning() {
        if (scanner == null) return;
        try {
            scanner.stopScan(scanCallback);
        } catch (RuntimeException exception) {
            Log.w(TAG, "stopping the scan threw " + exception);
        }
        forgetScanning();
    }

    /** One service UUID heard since the last call, each UUID once however often it was heard, or null when none is left. */
    public synchronized String takeSighting() {
        Iterator<String> oldest = unreadSightings.iterator();
        if (!oldest.hasNext()) return null;
        String uuid = oldest.next();
        oldest.remove();
        return uuid;
    }

    private void forgetAdvertising() {
        advertiser = null;
        advertiseCallback = null;
        advertisedServiceUuid = null;
    }

    private void forgetScanning() {
        scanner = null;
        scanCallback = null;
        scannedPrefixHex = null;
        unreadSightings.clear();
    }

    private BluetoothAdapter enabledAdapter() {
        BluetoothManager manager = context.getSystemService(BluetoothManager.class);
        BluetoothAdapter adapter = manager == null ? null : manager.getAdapter();
        return adapter != null && adapter.isEnabled() ? adapter : null;
    }

    private static String hexDigits(String uuidText) {
        return uuidText.replace("-", "").toLowerCase();
    }

    private static String repeated(char character, int count) {
        StringBuilder text = new StringBuilder();
        for (int i = 0; i < count; i++) text.append(character);
        return text.toString();
    }

    private static String padded(String hex, char padding) {
        return hex + repeated(padding, UUID_HEX_DIGITS - hex.length());
    }

    private static ParcelUuid uuidFromHex(String hex) {
        return ParcelUuid.fromString(hex.substring(0, 8) + "-" + hex.substring(8, 12) + "-" + hex.substring(12, 16)
                + "-" + hex.substring(16, 20) + "-" + hex.substring(20));
    }
}
