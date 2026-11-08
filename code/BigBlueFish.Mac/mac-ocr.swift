// On-device Apple Vision OCR. Reads one image on stdin and prints tesseract's TSV layout
// (level 5 rows), so the pet parses it exactly as the Linux tesseract output.
import Foundation
import ImageIO
import Vision

let data = FileHandle.standardInput.readDataToEndOfFile()
guard let source = CGImageSourceCreateWithData(data as CFData, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
    FileHandle.standardError.write("读屏图像无效。\n".data(using: .utf8)!); exit(2)
}
let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
request.recognitionLanguages = ["zh-Hans", "en-US"]
request.usesLanguageCorrection = true
do { try VNImageRequestHandler(cgImage: image).perform([request]) }
catch { FileHandle.standardError.write("\(error.localizedDescription)\n".data(using: .utf8)!); exit(3) }
let width = Double(image.width), height = Double(image.height)
var output = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
for (index, observation) in (request.results ?? []).enumerated() {
    guard let candidate = observation.topCandidates(1).first else { continue }
    let box = observation.boundingBox // normalized, bottom-left origin
    let text = candidate.string.replacingOccurrences(of: "\t", with: " ").replacingOccurrences(of: "\n", with: " ")
    output += "5\t1\t1\t1\t\(index + 1)\t1\t\(Int(box.minX * width))\t\(Int((1 - box.maxY) * height))\t\(Int(box.width * width))\t\(Int(box.height * height))\t\(Int(candidate.confidence * 100))\t\(text)\n"
}
FileHandle.standardOutput.write(output.data(using: .utf8)!)
