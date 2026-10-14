require 'xcodeproj'
require 'fileutils'
out = File.expand_path(ARGV.fetch(0))
FileUtils.mkdir_p(out)
project = Xcodeproj::Project.new(File.join(out, 'InteractionUITests.xcodeproj'))
target = project.new_target(:ui_test_bundle, 'InteractionUITests', :ios, '17.0')
file = project.main_group.new_file(File.expand_path(ARGV[1] || File.join(__dir__, 'InteractionUITests.swift')))
target.source_build_phase.add_file_reference(file)
target.build_configurations.each do |config|
  config.build_settings.merge!('PRODUCT_BUNDLE_IDENTIFIER' => 'com.dotnative.interactionuitests',
    'SWIFT_VERSION' => '5.0', 'GENERATE_INFOPLIST_FILE' => 'YES',
    'CODE_SIGNING_ALLOWED' => 'NO', 'TARGETED_DEVICE_FAMILY' => '1,2')
end
project.save
scheme = Xcodeproj::XCScheme.new
scheme.add_build_target(target)
scheme.add_test_target(target)
scheme.save_as(project.path, 'InteractionUITests')
