package com.anaglyph.wifidirect;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageManager;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.NetworkRequest;
import android.net.wifi.WifiManager;
import android.net.wifi.WifiNetworkSpecifier;
import android.net.wifi.WifiNetworkSuggestion;
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
 * A joiner connects as an ordinary Wi-Fi client, which Android asks the user to allow each time. Joined that way
 * it runs no network scans of its own, and the owner's scans interrupt the group for a few seconds instead of seven.
 * A device can own one group and be joined to another at once, each through its own instance.
 */
@SuppressLint("MissingPermission")
public final class WifiDirectGroup {
    public static final int IDLE = 0;
    public static final int FORMING = 1;
    public static final int READY = 2;
    public static final int FAILED = 3;

    private static final String TAG = "AnaglyphWifiDirectGroup";
    // every Android group owner hands out addresses in this range
    private static final String GROUP_ADDRESS_PREFIX = "192.168.49.";

    private final Context context;
    private final Activity activity;
    private final WifiP2pManager manager;
    private final WifiP2pManager.Channel channel;
    private final ConnectivityManager connectivity;
    private final WifiManager wifi;

    private int state = IDLE;
    private boolean isOwner;
    private String networkName;
    private String passphrase;
    private String interfaceName;
    private String ownerAddress;
    private String localAddress;
    private int frequencyMHz;
    private int requestedFrequencyMHz;
    private boolean bindsProcess;
    private boolean userRefused;
    private boolean joinsAsWifiDirectClient;
    private boolean joinsAsSuggestedNetwork;
    private WifiNetworkSuggestion suggestedGroup;
    private WifiManager.SuggestionUserApprovalStatusListener suggestionApprovalListener;
    private ConnectivityManager.NetworkCallback suggestedNetworkCallback;
    private ConnectivityManager.NetworkCallback joinedNetworkCallback;
    // every create, join and leave starts a new attempt, so late callbacks of an earlier one are ignored
    private int attempt;

    private static boolean leftoverGroupRemoved;

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
        activity = context instanceof Activity ? (Activity) context : null;
        manager = (WifiP2pManager) this.context.getSystemService(Context.WIFI_P2P_SERVICE);
        channel = manager == null ? null : manager.initialize(this.context, Looper.getMainLooper(), null);
        connectivity = (ConnectivityManager) this.context.getSystemService(Context.CONNECTIVITY_SERVICE);
        wifi = (WifiManager) this.context.getSystemService(Context.WIFI_SERVICE);
        // a suggested group outlives an app that was killed while joined to it, and the system would go on joining it
        if (!leftoverGroupRemoved) wifi.removeNetworkSuggestions(wifi.getNetworkSuggestions());
        if (channel == null) return;

        this.context.registerReceiver(connectionChanged,
                new IntentFilter(WifiP2pManager.WIFI_P2P_CONNECTION_CHANGED_ACTION), Context.RECEIVER_EXPORTED);
        // a group outlives an app that was killed while owning it
        if (!leftoverGroupRemoved) manager.removeGroup(channel, null);
        leftoverGroupRemoved = true;
    }

    /**
     * Starts a group owned by this device. With no frequency given it is on 5 GHz where the device allows it
     * and on 2.4 GHz otherwise; with one, it is on that frequency or fails.
     */
    public synchronized void create(String networkName, String passphrase, int frequencyMHz) {
        joinsAsWifiDirectClient = false;
        joinsAsSuggestedNetwork = false;
        requestedFrequencyMHz = frequencyMHz;
        begin(true, networkName, passphrase);
    }

    /**
     * Joins the group with this name and passphrase, once the user allows it. Nobody has to accept on the owner's side.
     * The group is not the device's default network: with bindProcess every socket this app opens afterwards uses it,
     * without it only sockets bound to localAddress() do.
     */
    public synchronized void join(String networkName, String passphrase, boolean bindProcess) {
        joinsAsWifiDirectClient = false;
        joinsAsSuggestedNetwork = false;
        bindsProcess = bindProcess;
        begin(false, networkName, passphrase);
    }

    /**
     * Joins the group as a Wi-Fi Direct client: the system asks the user nothing, but the device goes on scanning
     * for networks while joined, and cannot own a group of its own at the same time.
     */
    public synchronized void joinAsWifiDirectClient(String networkName, String passphrase) {
        joinsAsWifiDirectClient = true;
        joinsAsSuggestedNetwork = false;
        begin(false, networkName, passphrase);
    }

    /**
     * Joins as an ordinary Wi-Fi client by suggesting the group to the system, which then joins it by itself.
     * The system asks the user once, the first time this app suggests any network, and never per join.
     * FAILED when the user refused that, and when the device is on another Wi-Fi network, which the system
     * does not leave for a suggested one. Joined this way the owner's scans interrupt the group for
     * a few seconds instead of seven; the device still scans for networks itself.
     */
    public synchronized void joinAsSuggestedNetwork(String networkName, String passphrase) {
        joinsAsWifiDirectClient = false;
        joinsAsSuggestedNetwork = true;
        begin(false, networkName, passphrase);
    }

    public synchronized void leave() {
        attempt++;
        if (state == IDLE) return;
        state = IDLE;
        leaveJoinedNetwork();
        forgetSuggestedGroup();
        if (joinsAsWifiDirectClient) manager.cancelConnect(channel, null);
        if (isOwner || joinsAsWifiDirectClient) manager.removeGroup(channel, null);
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

    /** This device's own address inside a joined group, known once READY. */
    public synchronized String localAddress() {
        return localAddress;
    }

    /** The frequency of a group this device owns, known once READY. */
    public synchronized int frequencyMHz() {
        return frequencyMHz;
    }

    /** The last join ended FAILED because the user said no, or Android could not connect. */
    public synchronized boolean userRefused() {
        return userRefused;
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
        localAddress = null;
        frequencyMHz = 0;
        userRefused = false;
        leaveJoinedNetwork();
        forgetSuggestedGroup();

        if (channel == null) {
            state = FAILED;
            return;
        }
        state = FORMING;
        watchForAppClosing(true);

        if (!asOwner && !joinsAsWifiDirectClient) {
            start(thisAttempt);
            return;
        }

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
            if (watch) WifiDirectGroupTaskWatch.watchedGroups.add(this);
            else WifiDirectGroupTaskWatch.watchedGroups.remove(this);
            if (watch) context.startService(taskWatch);
            else if (WifiDirectGroupTaskWatch.watchedGroups.isEmpty()) context.stopService(taskWatch);
        } catch (RuntimeException exception) {
            // Android refuses to start a service for an app that isn't in front
            Log.w(TAG, "task watch threw " + exception);
        }
    }

    private synchronized void start(int thisAttempt) {
        if (thisAttempt != attempt) return;
        if (joinsAsSuggestedNetwork) suggestGroup(thisAttempt);
        else if (joinsAsWifiDirectClient) connectAsWifiDirectClient(thisAttempt);
        else if (!isOwner) joinAsWifiClient(thisAttempt);
        else if (requestedFrequencyMHz != 0) createOnFrequency(thisAttempt);
        else createOnBand(WifiP2pConfig.GROUP_OWNER_BAND_5GHZ, thisAttempt);
    }

    private void suggestGroup(final int thisAttempt) {
        suggestedGroup = new WifiNetworkSuggestion.Builder().setSsid(networkName).setWpa2Passphrase(passphrase).build();
        int added = wifi.addNetworkSuggestions(Collections.singletonList(suggestedGroup));
        if (added != WifiManager.STATUS_NETWORK_SUGGESTIONS_SUCCESS) {
            Log.w(TAG, "suggesting " + networkName + " failed with status " + added);
            state = FAILED;
            return;
        }

        suggestionApprovalListener = approval -> {
            synchronized (WifiDirectGroup.this) {
                if (thisAttempt != attempt || approval != WifiManager.STATUS_SUGGESTION_APPROVAL_REJECTED_BY_USER) return;
                Log.w(TAG, "the user does not let this app suggest networks");
                userRefused = true;
                if (state == FORMING) state = FAILED;
            }
        };
        wifi.addSuggestionUserApprovalStatusListener(context.getMainExecutor(), suggestionApprovalListener);

        // The system joins a suggested network only when one of its scans finds it, which can be minutes away.
        // Asking for any Wi-Fi network, trusted by the user or not, makes it scan now.
        NetworkRequest anyWifiNetwork = new NetworkRequest.Builder()
                .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
                .removeCapability(NetworkCapabilities.NET_CAPABILITY_TRUSTED)
                .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET).build();

        suggestedNetworkCallback = new ConnectivityManager.NetworkCallback() {
            @Override
            public void onLinkPropertiesChanged(Network network, LinkProperties link) {
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt != attempt || link.getDhcpServerAddress() == null) return;
                    String addressGiver = link.getDhcpServerAddress().getHostAddress();
                    if (!addressGiver.startsWith(GROUP_ADDRESS_PREFIX)) {
                        Log.w(TAG, "on another Wi-Fi network, which the system keeps over a suggested one");
                        if (state == FORMING) state = FAILED;
                        return;
                    }
                    if (ownerAddress == null) Log.i(TAG, "joined " + networkName + " as a suggested network");
                    interfaceName = link.getInterfaceName();
                    ownerAddress = addressGiver;
                    for (LinkAddress address : link.getLinkAddresses())
                        if (address.getAddress() instanceof Inet4Address) localAddress = address.getAddress().getHostAddress();
                }
            }

            @Override
            public void onLost(Network network) {
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt != attempt || state != READY) return;
                    Log.w(TAG, "group lost");
                    state = FAILED;
                }
            }
        };
        connectivity.requestNetwork(anyWifiNetwork, suggestedNetworkCallback);
    }

    /** Taking the suggestion back also makes the system leave the group. */
    private void forgetSuggestedGroup() {
        if (suggestedGroup == null) return;
        wifi.removeNetworkSuggestions(Collections.singletonList(suggestedGroup));
        suggestedGroup = null;
        if (suggestionApprovalListener != null) wifi.removeSuggestionUserApprovalStatusListener(suggestionApprovalListener);
        suggestionApprovalListener = null;
        if (suggestedNetworkCallback != null) connectivity.unregisterNetworkCallback(suggestedNetworkCallback);
        suggestedNetworkCallback = null;
    }

    private void connectAsWifiDirectClient(final int thisAttempt) {
        manager.connect(channel, config().build(), new WifiP2pManager.ActionListener() {
            @Override
            public void onSuccess() {
            }

            @Override
            public void onFailure(int reason) {
                Log.w(TAG, "join failed with reason " + reason);
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt == attempt) state = FAILED;
                }
            }
        });
    }

    private void joinAsWifiClient(final int thisAttempt) {
        WifiNetworkSpecifier group = new WifiNetworkSpecifier.Builder()
                .setSsid(networkName).setWpa2Passphrase(passphrase).build();
        NetworkRequest request = new NetworkRequest.Builder()
                .addTransportType(NetworkCapabilities.TRANSPORT_WIFI)
                .removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                .setNetworkSpecifier(group).build();

        joinedNetworkCallback = new ConnectivityManager.NetworkCallback() {
            @Override
            public void onAvailable(Network network) {
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt != attempt) return;
                    if (bindsProcess) connectivity.bindProcessToNetwork(network);
                    returnToFront();
                    Log.i(TAG, "joined " + networkName + " as a Wi-Fi client");
                }
            }

            @Override
            public void onLinkPropertiesChanged(Network network, LinkProperties link) {
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt != attempt || link.getDhcpServerAddress() == null) return;
                    interfaceName = link.getInterfaceName();
                    ownerAddress = link.getDhcpServerAddress().getHostAddress();
                    for (LinkAddress address : link.getLinkAddresses())
                        if (address.getAddress() instanceof Inet4Address) localAddress = address.getAddress().getHostAddress();
                }
            }

            @Override
            public void onUnavailable() {
                Log.w(TAG, "join refused or failed");
                returnToFront();
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt != attempt) return;
                    userRefused = true;
                    state = FAILED;
                }
            }

            @Override
            public void onLost(Network network) {
                Log.w(TAG, "group lost");
                synchronized (WifiDirectGroup.this) {
                    if (thisAttempt == attempt) state = FAILED;
                }
            }
        };
        connectivity.requestNetwork(request, joinedNetworkCallback);
    }

    /**
     * The system's question about joining opens over the app, and on a headset it leaves the system's windows
     * open after it is answered. Starting the app's own activity again closes them.
     */
    private void returnToFront() {
        if (activity == null || activity.hasWindowFocus()) return;
        Intent launch = context.getPackageManager().getLaunchIntentForPackage(context.getPackageName());
        if (launch == null) return;
        try {
            context.startActivity(launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        } catch (RuntimeException exception) {
            Log.w(TAG, "return to front threw " + exception);
        }
    }

    private void leaveJoinedNetwork() {
        if (joinedNetworkCallback == null) return;
        if (bindsProcess) connectivity.bindProcessToNetwork(null);
        connectivity.unregisterNetworkCallback(joinedNetworkCallback);
        joinedNetworkCallback = null;
        // an unanswered question is still open over the app
        returnToFront();
    }

    private void createOnFrequency(final int thisAttempt) {
        manager.createGroup(channel, config().setGroupOperatingFrequency(requestedFrequencyMHz).build(),
                new WifiP2pManager.ActionListener() {
                    @Override
                    public void onSuccess() {
                    }

                    @Override
                    public void onFailure(int reason) {
                        Log.w(TAG, "create at " + requestedFrequencyMHz + " MHz failed with reason " + reason);
                        synchronized (WifiDirectGroup.this) {
                            if (thisAttempt == attempt) state = FAILED;
                        }
                    }
                });
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

    private synchronized void readGroup() {
        if (!(isOwner || joinsAsWifiDirectClient) || state == IDLE || state == FAILED) return;
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
                    frequencyMHz = group.getFrequency();
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
