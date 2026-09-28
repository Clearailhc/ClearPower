import Testing
@testable import ClearPowerApp

struct SankeyTests {
    @Test func zeroInputHasNoAdapterNodeOrFlow() {
        let model = SankeyModel()
        let graph = model.model(["on_ac": true, "bat_w": -12.7, "sys_w": 12.7, "adapter_w": 0.0])
        #expect(graph.nodes["adapter"] == nil)
        #expect(!graph.flows.contains { $0.a == "adapter" })
        #expect(graph.nodes["battery"]?.w == 12.7)
    }

    @Test func sensorValueIsNotReconstructedFromRoundedOrEasedTotals() {
        let model = SankeyModel()
        let graph = model.model(["on_ac": true, "bat_w": -12.7, "sys_w": 13.2,
                                 "adapter_w": 0.444, "adapter_input_disabled": true])
        #expect(graph.nodes["adapter"]?.w == 0.444)
        #expect(graph.nodes["adapter"]?.labelKey == "adapterReading")
        #expect(nodeTipKey(graph.nodes["adapter"]!) == "tipAdapterDisabled")
        #expect(fmtW(0.004) == "<0.1 W")
    }

    @Test func unplugAndDisableSnapAnimationsToNewSourceState() {
        let model = SankeyModel()
        model.update(["on_ac": true, "bat_w": 35.0, "adapter_w": 50.0])
        model.update(["on_ac": true, "adapter_input_disabled": true, "bat_w": -12.7, "adapter_w": 0.444])
        #expect(!model.sheenEnabled())
        #expect(model.shown?["adapter_w"] as? Double == 0.444)
        model.update(["on_ac": false, "bat_w": -12.7, "adapter_w": 0.0])
        let graph = model.model(model.shown!)
        #expect(graph.nodes["adapter"] == nil)
    }
}
