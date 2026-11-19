import { useEffect, useRef } from "react";
import {
    HubConnectionBuilder,
    HubConnectionState,
    LogLevel,
} from "@microsoft/signalr";
import { apiBaseUrl } from "./api";

export function useKitchenUpdates(onKitchenChanged: () => void): void {
    const callbackRef = useRef(onKitchenChanged);
    callbackRef.current = onKitchenChanged;

    useEffect(() => {
        let disposed = false;
        let retryTimer: ReturnType<typeof setTimeout> | undefined;

        const connection = new HubConnectionBuilder()
            .withUrl(`${apiBaseUrl}/hubs/kitchen`, {
                withCredentials: false,
            })
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(LogLevel.Warning)
            .build();

        const refreshKitchen = () => {
            if (!disposed) callbackRef.current();
        };

        const connect = async () => {
            if (disposed ||
                connection.state !== HubConnectionState.Disconnected) {
                return;
            }

            try {
                await connection.start();
                if (disposed) {
                    await connection.stop();
                    return;
                }
                refreshKitchen();
            } catch (error) {
                if (!disposed) {
                    console.warn(
                        "Live kitchen connection failed. Retrying...",
                        error,
                    );
                    retryTimer = setTimeout(() => void connect(), 5000);
                }
            }
        };

        connection.on("KitchenOrdersChanged", refreshKitchen);
        connection.onreconnected(refreshKitchen);
        connection.onclose(() => {
            if (!disposed) {
                retryTimer = setTimeout(() => void connect(), 5000);
            }
        });

        void connect();

        return () => {
            disposed = true;
            if (retryTimer !== undefined) clearTimeout(retryTimer);
            connection.off("KitchenOrdersChanged", refreshKitchen);
            void connection.stop();
        };
    }, []);
}
