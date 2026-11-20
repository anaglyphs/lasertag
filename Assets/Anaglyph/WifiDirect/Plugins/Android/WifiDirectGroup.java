package com.anaglyph.wifidirect;

import android.annotation.SuppressLint;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageManager;
import android.net.wifi.p2p.WifiP2pConfig;
import android.net.wifi.p2p.WifiP2pManager;
import android.os.Build;
import android.os.Looper;
import android.util.Log;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.util.Collections;

/**
 * One Wi-Fi Direct group, created by this device as its owner or joined by network name and passphrase.
 * A group is a Wi-Fi network without a router: the owner is its access point and hands out addresses.
 */
@SuppressLint("MissingPermission")
public final class WifiDirectGroup {
    public static final int IDLE = 0;
    public static final int FORMING = 1;
    public static final int READY = 2;
    public static final int FAILED = 3;

    private static final String TAG = "AnaglyphWifiDirectGroup";

    private final Context context;
    private final WifiP2pManager manager;
    private final WifiP2pManager.Channel channel;

    private int state = IDLE;
    private boolean isOwner;
    private String networkName;
    private String passphrase;
    private String interfaceName;
    private String ownerAddress;
    // every create, join and leave starts a new attempt, so late callbacks of an earlier one are ignored
    private int attempt;

    private final BroadcastReceiver connectionChanged = new BroadcastReceiver() {
        @Override
        public void onReceive(Context receivingContext, Intent intent) {
            readGroup();
        }
    };

    public static boolean isSupported(Context context) {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU
                && context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_WIFI_DIRECT);
    }

    public WifiDirectGroup(Context context) {
        this.context = context.getApplicationContext();
        manager = (WifiP2pManager) this.context.getSystemService(Context.WIFI_P2P_SERVICE);
        channel = manager == null ? null : manager.initialize(this.context, Looper.getMainLooper(), null);
        if (channel == null) return;

        this.context.registerReceiver(connectionChanged,
                new IntentFilter(WifiP2pManager.WIFI_P2P_CONNECTION_CHANGED_ACTION), Context.RECEIVER_EXPORTED);
        // a group outlives an app that was killed while owning it
        manager.removeGroup(channel, null);
    }

    /** Starts a group owned by this device, on 5 GHz where the device allows it and on 2.4 GHz otherwise. */
    public synchronized void create(String networkName, String passphrase) {
        begin(true, networkName, passphrase);
    }

    /** Joins the group with this name and passphrase. Nobody has to accept on the owner's side. */
    public synchronized void join(String networkName, String passphrase) {
        begin(false, networkName, passphrase);
    }

    public synchronized void leave() {
        attempt++;
        if (state == IDLE) return;
        state = IDLE;
        manager.cancelConnect(channel, null);
        manager.removeGroup(channel, null);
        watchForAppClosing(false);
    }

    /** READY once traffic can flow: the owner's group is up, or the joiner has its address. */
    public synchronized int state() {
        if (state == FORMING && interfaceName != null && ownerAddress != null && hasIPv4Address(interfaceName))
            state = READY;
        return state;
    }

    /** The owner's address inside the group, known once READY. */
    public synchronized String ownerAddress() {
        return ownerAddress;
    }

    public synchronized void dispose() {
        if (channel == null) return;
        leave();
        context.unregisterReceiver(connectionChanged);
    }

    private void begin(boolean asOwner, String newNetworkName, String newPassphrase) {
        final int thisAttempt = ++attempt;
        isOwner = asOwner;
        networkName = newNetworkName;
        passphrase = newPassphrase;
        interfaceName = null;
        ownerAddress = null;

        if (channel == null) {
            state = FAILED;
            return;
        }
        state = FORMING;
        watchForAppClosing(true);

        // a group still up would make this one fail as busy
        manager.removeGroup(channel, new WifiP2pManager.ActionListener() {
            @Override
            public void onSuccess() {
                start(thisAttempt);
            }

            @Override
            public void onFailure(int reason) {
                start(thisAttempt);
            }
        });
    }

    private void watchForAppClosing(boolean watch) {
        Intent taskWatch = new Intent(context, WifiDirectGroupTaskWatch.class);
        try {
            WifiDirectGroupTaskWatch.watchedGroup = watch ? this : null;
            if (watch) context.startService(taskWatch);
            else context.stopService(taskWatch);
        } catch (RuntimeException exception) {
            // Android refuses to start a service for an app that isn't in front
            Log.w(TAG, "task watch threw " + exception);
        }
    }

    private synchronized void start(int thisAttempt) {
        if (thisAttempt != attempt) return;
        if (isOwner) createOnBand(WifiP2pConfig.GROUP_OWNER_BAND_5GHZ, thisAttempt);
        else manager.connect(channel, config().build(), failing("join", thisAttempt));
    }

    private void createOnBand(final int band, final int thisAttempt) {
        manager.createGroup(channel, config().setGroupOperatingBand(band).build(), new WifiP2pManager.ActionListener() {
            @Override
            public void onSuccess() {
            }

            @Override
            public void onFailure(int reason) {
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt != attempt) return;
                    if (band == WifiP2pConfig.GROUP_OWNER_BAND_5GHZ) {
                        Log.i(TAG, "5 GHz group refused (reason " + reason + "), using 2.4 GHz");
                        createOnBand(WifiP2pConfig.GROUP_OWNER_BAND_2GHZ, thisAttempt);
                    } else {
                        Log.w(TAG, "create failed with reason " + reason);
                        state = FAILED;
                    }
                }
            }
        });
    }

    private WifiP2pConfig.Builder config() {
        return new WifiP2pConfig.Builder().setNetworkName(networkName).setPassphrase(passphrase);
    }

    private WifiP2pManager.ActionListener failing(final String step, final int thisAttempt) {
        return new WifiP2pManager.ActionListener() {
            @Override
            public void onSuccess() {
            }

            @Override
            public void onFailure(int reason) {
                Log.w(TAG, step + " failed with reason " + reason);
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt == attempt) state = FAILED;
                }
            }
        };
    }

    private synchronized void readGroup() {
        if (state == IDLE || state == FAILED) return;
        final int thisAttempt = attempt;

        manager.requestGroupInfo(channel, group -> {
            synchronized (WifiDirectGroup.this) {
                if (thisAttempt != attempt) return;
                boolean isThisGroup = group != null && networkName.equals(group.getNetworkName())
                        && group.isGroupOwner() == isOwner;
                if (isThisGroup) {
                    if (interfaceName == null)
                        Log.i(TAG, (isOwner ? "created " : "joined ") + networkName + " at " + group.getFrequency() + " MHz");
                    interfaceName = group.getInterface();
                } else if (state == READY) {
                    Log.w(TAG, "group lost");
                    interfaceName = null;
                    state = FAILED;
                }
            }
        });

        manager.requestConnectionInfo(channel, info -> {
            synchronized (WifiDirectGroup.this) {
                if (thisAttempt != attempt || !info.groupFormed || info.groupOwnerAddress == null) return;
                ownerAddress = info.groupOwnerAddress.getHostAddress();
            }
        });
    }

    private static boolean hasIPv4Address(String interfaceName) {
        try {
            NetworkInterface networkInterface = NetworkInterface.getByName(interfaceName);
            if (networkInterface == null) return false;
            for (InetAddress address : Collections.list(networkInterface.getInetAddresses()))
                if (address instanceof Inet4Address) return true;
        } catch (Exception exception) {
            Log.w(TAG, "reading " + interfaceName + " threw " + exception);
        }
        return false;
    }
}
