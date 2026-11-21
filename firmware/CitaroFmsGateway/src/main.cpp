#include <Arduino.h>
#include <ArduinoJson.h>
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLEUtils.h>
#include <driver/twai.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

// Waveshare ESP32-S3-RS485-CAN / -U. Die Vorlage mit GPIO 13/14 gilt für ein anderes Board.
static constexpr gpio_num_t CAN_TX_PIN = GPIO_NUM_15;
static constexpr gpio_num_t CAN_RX_PIN = GPIO_NUM_16;

// RS485-Klemmen A/B. GPIO21 bleibt Low, der Baustein sendet nicht.
static constexpr int RS485_TX_PIN = 17;
static constexpr int RS485_RX_PIN = 18;
static constexpr int RS485_DE_PIN = 21;
static constexpr uint32_t kTestBaud = 9600;

// Dieselben Kennungen wie FmsSignals in der App.
static constexpr char kDeviceName[] = "Citaro_FMS_Gateway";
static constexpr char kServiceUuid[] = "4fa2c730-1f8a-40a4-9e32-261f8a40a49e";
static constexpr char kCharacteristicUuid[] = "beb5483e-36e1-4688-b7f5-ea07361b26a8";

static constexpr uint32_t kFreshMs = 5000;
static constexpr uint32_t kNotifyMs = 1000;

static constexpr uint32_t PGN_EEC2 = 61443;
static constexpr uint32_t PGN_EEC1 = 61444;
static constexpr uint32_t PGN_HRLFC = 64777;
static constexpr uint32_t PGN_DC2 = 64933;
static constexpr uint32_t PGN_DC1 = 65102;
static constexpr uint32_t PGN_CITARO_DOORS = 65106;
static constexpr uint32_t PGN_AT1 = 65110;
static constexpr uint32_t PGN_SERV = 65216;
static constexpr uint32_t PGN_VDHR = 65217;
static constexpr uint32_t PGN_HOURS = 65253;
static constexpr uint32_t PGN_LFC = 65257;
static constexpr uint32_t PGN_ET1 = 65262;
static constexpr uint32_t PGN_CCVS = 65265;
static constexpr uint32_t PGN_LFE = 65266;
static constexpr uint32_t PGN_AMB = 65269;
static constexpr uint32_t PGN_VEP1 = 65271;
static constexpr uint32_t PGN_DD1 = 65276;

struct Sample {
    bool known = false;
    uint32_t at = 0;
    double value = 0;

    void set(double next) {
        value = next;
        at = millis();
        known = true;
    }

    bool live(uint32_t now) const {
        return known && (now - at) < kFreshMs;
    }
};

static Sample speed;
static Sample tank;
static Sample fuelRate;
static Sample fuelUsed;
static Sample km;
static Sample doorCriterion;
static Sample t1;
static Sample t2;
static Sample t3;
static Sample rpm;
static Sample coolant;
static Sample ambient;
static Sample engineHours;
static Sample accelerator;
static Sample brakeSw;
static Sample parkingBrake;
static Sample clutch;
static Sample cruise;
static Sample ignition;
static Sample adblue;
static Sample batteryVoltage;
static Sample serviceKm;
static bool fuelUsedHighRes = false;
static uint32_t canFrames = 0;
static uint32_t testLines = 0;
static bool canReady = false;
static char testLine[160];
static size_t testLineLen = 0;
static bool deviceConnected = false;
static BLECharacteristic *characteristic = nullptr;

static uint16_t read16(const uint8_t *data, int index) {
    return (uint16_t)data[index] | ((uint16_t)data[index + 1] << 8);
}

static uint32_t read32(const uint8_t *data, int index) {
    return (uint32_t)data[index] |
           ((uint32_t)data[index + 1] << 8) |
           ((uint32_t)data[index + 2] << 16) |
           ((uint32_t)data[index + 3] << 24);
}

static bool ok8(uint8_t value) { return value < 0xFE; }
static bool ok16(uint16_t value) { return value < 0xFE00; }
static bool ok32(uint32_t value) { return value < 0xFE000000; }

static int twoBits(uint8_t byte, int shift) { return (byte >> shift) & 0x03; }

static void setSwitch(Sample &sample, int code) {
    if (code == 0 || code == 1) {
        sample.set(code);
    }
}

static void setDoor(Sample &sample, int code) { setSwitch(sample, code); }

static uint32_t pgnOf(uint32_t identifier) {
    const uint8_t pf = (identifier >> 16) & 0xFF;
    if (pf < 240) {
        return (identifier >> 8) & 0x3FF00;
    }
    return (identifier >> 8) & 0x3FFFF;
}

static void noteIgnition() { ignition.set(1); }

class ServerCallbacks : public BLEServerCallbacks {
    void onConnect(BLEServer *server) override {
        deviceConnected = true;
        (void)server;
        Serial.println("BLE verbunden");
    }

    void onDisconnect(BLEServer *server) override {
        deviceConnected = false;
        (void)server;
        Serial.println("BLE getrennt");
        BLEDevice::startAdvertising();
    }
};

static bool startCan() {
    twai_general_config_t general = TWAI_GENERAL_CONFIG_DEFAULT(CAN_TX_PIN, CAN_RX_PIN, TWAI_MODE_LISTEN_ONLY);
    twai_timing_config_t timing = TWAI_TIMING_CONFIG_250KBITS();
    twai_filter_config_t filter = TWAI_FILTER_CONFIG_ACCEPT_ALL();
    if (twai_driver_install(&general, &timing, &filter) != ESP_OK) {
        Serial.println("CAN-Treiber nicht installiert");
        return false;
    }
    if (twai_start() != ESP_OK) {
        Serial.println("CAN nicht gestartet");
        twai_driver_uninstall();
        return false;
    }
    Serial.println("CAN hört mit, 250 kbit, GPIO15/16, sendet nichts");
    return true;
}

static void handleFrame(const twai_message_t &message) {
    if (!message.extd || message.data_length_code == 0) {
        return;
    }

    canFrames++;
    noteIgnition();
    const uint8_t *data = message.data;
    const uint8_t length = message.data_length_code;
    const uint32_t pgn = pgnOf(message.identifier);

    switch (pgn) {
    case PGN_CCVS:
        if (length >= 3) {
            const uint16_t raw = read16(data, 1);
            if (ok16(raw)) {
                speed.set(raw / 256.0);
            }
        }
        if (length >= 4) {
            setSwitch(parkingBrake, twoBits(data[0], 2));
            setSwitch(cruise, twoBits(data[3], 0));
            setSwitch(brakeSw, twoBits(data[3], 4));
            setSwitch(clutch, twoBits(data[3], 6));
        }
        break;
    case PGN_DD1:
        if (length >= 2 && data[1] <= 250) {
            tank.set(data[1] * 0.4);
        }
        break;
    case PGN_LFE:
        if (length >= 2) {
            const uint16_t raw = read16(data, 0);
            if (ok16(raw)) {
                fuelRate.set(raw * 0.05);
            }
        }
        break;
    case PGN_HRLFC:
        if (length >= 8) {
            const uint32_t raw = read32(data, 4);
            if (ok32(raw)) {
                fuelUsed.set(raw * 0.001);
                fuelUsedHighRes = true;
            }
        }
        break;
    case PGN_LFC:
        if (!fuelUsedHighRes && length >= 8) {
            const uint32_t raw = read32(data, 4);
            if (ok32(raw)) {
                fuelUsed.set(raw * 0.5);
            }
        }
        break;
    case PGN_VDHR:
        if (length >= 4) {
            const uint32_t raw = read32(data, 0);
            if (ok32(raw)) {
                km.set(raw * 0.005);
            }
        }
        break;
    case PGN_DC1:
        if (length >= 1) {
            setSwitch(doorCriterion, twoBits(data[0], 0));
        }
        break;
    case PGN_DC2:
        if (length >= 2) {
            setDoor(t1, twoBits(data[0], 2));
            setDoor(t2, twoBits(data[1], 0));
            setDoor(t3, twoBits(data[1], 6));
        }
        break;
    case PGN_CITARO_DOORS:
        if (length >= 1) {
            setDoor(t1, twoBits(data[0], 0));
            setDoor(t2, twoBits(data[0], 2));
            setDoor(t3, twoBits(data[0], 4));
        }
        break;
    case PGN_EEC1:
        if (length >= 5) {
            const uint16_t raw = read16(data, 3);
            if (ok16(raw)) {
                rpm.set(raw * 0.125);
            }
        }
        break;
    case PGN_ET1:
        if (length >= 1 && ok8(data[0])) {
            coolant.set((int)data[0] - 40);
        }
        break;
    case PGN_AMB:
        if (length >= 5) {
            const uint16_t raw = read16(data, 3);
            if (ok16(raw)) {
                ambient.set(raw * 0.03125 - 273.0);
            }
        }
        break;
    case PGN_HOURS:
        if (length >= 4) {
            const uint32_t raw = read32(data, 0);
            if (ok32(raw)) {
                engineHours.set(raw * 0.05);
            }
        }
        break;
    case PGN_EEC2:
        if (length >= 2 && data[1] <= 250) {
            accelerator.set(data[1] * 0.4);
        }
        break;
    case PGN_AT1:
        if (length >= 1 && data[0] <= 250) {
            adblue.set(data[0] * 0.4);
        }
        break;
    case PGN_VEP1:
        if (length >= 8) {
            const uint16_t raw = read16(data, 6);
            if (ok16(raw)) {
                batteryVoltage.set(raw * 0.05);
            }
        }
        break;
    case PGN_SERV:
        if (length >= 3) {
            const uint16_t raw = read16(data, 1);
            if (ok16(raw)) {
                serviceKm.set(raw * 5.0 - 160635.0);
            }
        }
        break;
    default:
        break;
    }
}

static bool applyTestKey(const char *key, double value) {
    if (strcmp(key, "speed") == 0) {
        speed.set(value);
    } else if (strcmp(key, "tank") == 0) {
        tank.set(value);
    } else if (strcmp(key, "fuelRate") == 0) {
        fuelRate.set(value);
    } else if (strcmp(key, "fuelUsed") == 0) {
        fuelUsed.set(value);
    } else if (strcmp(key, "km") == 0) {
        km.set(value);
    } else if (strcmp(key, "rpm") == 0) {
        rpm.set(value);
    } else if (strcmp(key, "coolant") == 0) {
        coolant.set(value);
    } else if (strcmp(key, "ambient") == 0) {
        ambient.set(value);
    } else if (strcmp(key, "engineHours") == 0) {
        engineHours.set(value);
    } else if (strcmp(key, "accelerator") == 0) {
        accelerator.set(value);
    } else if (strcmp(key, "adblue") == 0) {
        adblue.set(value);
    } else if (strcmp(key, "batteryVoltage") == 0) {
        batteryVoltage.set(value);
    } else if (strcmp(key, "serviceKm") == 0) {
        serviceKm.set(value);
    } else if (strcmp(key, "doorCriterion") == 0) {
        doorCriterion.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "t1") == 0 || strcmp(key, "t1_open") == 0) {
        t1.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "t2") == 0 || strcmp(key, "t2_open") == 0) {
        t2.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "t3") == 0 || strcmp(key, "t3_open") == 0) {
        t3.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "brake") == 0) {
        brakeSw.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "parkingBrake") == 0) {
        parkingBrake.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "clutch") == 0) {
        clutch.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "cruise") == 0) {
        cruise.set(value >= 0.5 ? 1 : 0);
    } else if (strcmp(key, "ignition") == 0) {
        ignition.set(value >= 0.5 ? 1 : 0);
    } else {
        return false;
    }
    return true;
}

static void handleTestLine(char *line) {
    int applied = 0;
    char *token = strtok(line, " \t");
    while (token != nullptr) {
        char *eq = strchr(token, '=');
        if (eq != nullptr && eq != token) {
            *eq = 0;
            if (applyTestKey(token, atof(eq + 1))) {
                applied++;
            }
        }
        token = strtok(nullptr, " \t");
    }
    if (applied == 0) {
        Serial.println("RS485 Zeile ohne bekannten Wert");
        return;
    }
    testLines++;
    noteIgnition();
    Serial.printf("RS485 Test #%lu, %d Werte\n", static_cast<unsigned long>(testLines), applied);
}

static void startRs485() {
    pinMode(RS485_DE_PIN, OUTPUT);
    digitalWrite(RS485_DE_PIN, LOW);
    Serial1.begin(kTestBaud, SERIAL_8N1, RS485_RX_PIN, RS485_TX_PIN);
    Serial.println("RS485 hört auf Testdaten, 9600 8N1, Klemmen A/B, sendet nichts");
}

static void pollRs485() {
    while (Serial1.available() > 0) {
        const int raw = Serial1.read();
        if (raw < 0) {
            return;
        }
        const char c = static_cast<char>(raw);
        if (c == '\r') {
            continue;
        }
        if (c == '\n') {
            testLine[testLineLen] = 0;
            if (testLineLen > 0) {
                handleTestLine(testLine);
            }
            testLineLen = 0;
            continue;
        }
        if (testLineLen + 1 < sizeof(testLine)) {
            testLine[testLineLen++] = c;
        }
    }
}

static void pollCan() {
    if (!canReady) {
        return;
    }
    twai_message_t message;
    while (twai_receive(&message, 0) == ESP_OK) {
        handleFrame(message);
    }
}

static void putNumber(JsonDocument &doc, const char *key, Sample &sample, uint32_t now, int decimals) {
    if (!sample.live(now)) {
        return;
    }
    const double scale = pow(10.0, decimals);
    doc[key] = round(sample.value * scale) / scale;
}

static void putFlag(JsonDocument &doc, const char *key, Sample &sample, uint32_t now) {
    if (!sample.live(now)) {
        return;
    }
    doc[key] = sample.value >= 0.5;
}

static void publish() {
    const uint32_t now = millis();
    const bool doorOpen =
        (t1.live(now) && t1.value >= 0.5) ||
        (t2.live(now) && t2.value >= 0.5) ||
        (t3.live(now) && t3.value >= 0.5);
    if (doorOpen) {
        doorCriterion.set(1);
    }

    JsonDocument doc;
    putNumber(doc, "speed", speed, now, 1);
    putNumber(doc, "tank", tank, now, 1);
    putNumber(doc, "fuelRate", fuelRate, now, 1);
    putNumber(doc, "fuelUsed", fuelUsed, now, 1);
    putNumber(doc, "km", km, now, 1);
    putFlag(doc, "doorCriterion", doorCriterion, now);
    putFlag(doc, "t1_open", t1, now);
    putFlag(doc, "t2_open", t2, now);
    putFlag(doc, "t3_open", t3, now);
    putNumber(doc, "rpm", rpm, now, 0);
    putNumber(doc, "coolant", coolant, now, 0);
    putNumber(doc, "ambient", ambient, now, 1);
    putNumber(doc, "engineHours", engineHours, now, 1);
    putNumber(doc, "accelerator", accelerator, now, 0);
    putFlag(doc, "brake", brakeSw, now);
    putFlag(doc, "parkingBrake", parkingBrake, now);
    putFlag(doc, "clutch", clutch, now);
    putFlag(doc, "cruise", cruise, now);
    putFlag(doc, "ignition", ignition, now);
    if (rpm.live(now)) {
        doc["engineOn"] = rpm.value >= 400.0;
    }
    putNumber(doc, "adblue", adblue, now, 0);
    putNumber(doc, "batteryVoltage", batteryVoltage, now, 1);
    putNumber(doc, "serviceKm", serviceKm, now, 0);
    doc["canFrames"] = canFrames;
    doc["testLines"] = testLines;

    if (fuelUsedHighRes && !fuelUsed.live(now)) {
        fuelUsedHighRes = false;
    }

    String payload;
    serializeJson(doc, payload);
    if (characteristic != nullptr && payload.length() <= 500) {
        characteristic->setValue(payload.c_str());
        if (deviceConnected) {
            characteristic->notify();
        }
    }
    Serial.println(payload);
}

static void startBle() {
    BLEDevice::init(kDeviceName);
    BLEDevice::setMTU(517);
    BLEDevice::setPower(ESP_PWR_LVL_P3);
    BLEServer *server = BLEDevice::createServer();
    server->setCallbacks(new ServerCallbacks());
    BLEService *service = server->createService(kServiceUuid);
    characteristic = service->createCharacteristic(
        kCharacteristicUuid,
        BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
    service->start();
    BLEAdvertising *advertising = BLEDevice::getAdvertising();
    advertising->addServiceUUID(kServiceUuid);
    advertising->setScanResponse(true);
    advertising->setName(kDeviceName);
    BLEDevice::startAdvertising();
    Serial.println("BLE sichtbar als Citaro_FMS_Gateway");
}

void setup() {
    Serial.begin(115200);
    delay(400);
    Serial.println("Citaro FMS Gateway, ESP32-S3-RS485-CAN-U");
    canReady = startCan();
    startRs485();
    startBle();
}

void loop() {
    static uint32_t nextCanRetry = 0;
    static uint32_t nextPublish = 0;
    const uint32_t now = millis();

    if (!canReady && now >= nextCanRetry) {
        canReady = startCan();
        nextCanRetry = now + 2000;
    }

    pollCan();
    pollRs485();

    if (now >= nextPublish) {
        nextPublish = now + kNotifyMs;
        publish();
    }
}
