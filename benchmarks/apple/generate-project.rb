require 'xcodeproj'
require 'fileutils'
out = File.expand_path(ARGV.fetch(0))
platform = ARGV.fetch(1, 'ios').to_sym
raise 'Expected ios or osx' unless [:ios, :osx].include?(platform)
FileUtils.mkdir_p(out)
project = Xcodeproj::Project.new(File.join(out, 'PerformanceTests.xcodeproj'))
target = project.new_target(:ui_test_bundle, 'PerformanceTests', platform, platform == :ios ? '17.0' : '13.0')
file = project.main_group.new_file(File.expand_path('PerformanceTests.swift', __dir__))
target.source_build_phase.add_file_reference(file)
target.build_configurations.each do |config|
  config.build_settings.merge!('PRODUCT_BUNDLE_IDENTIFIER' => 'com.dotnative.performancetests',
    'SWIFT_VERSION' => '5.0', 'GENERATE_INFOPLIST_FILE' => 'YES', 'CODE_SIGNING_ALLOWED' => 'NO')
  config.build_settings['TARGETED_DEVICE_FAMILY'] = '1,2' if platform == :ios
end
project.save
scheme = Xcodeproj::XCScheme.new
scheme.add_build_target(target)
scheme.add_test_target(target)
# Environment values are fixed in the generated scheme for reproducibility.
scheme.test_action.environment_variables = Xcodeproj::XCScheme::EnvironmentVariables.new([
  { key: 'BENCH_BUNDLE_ID', value: ARGV.fetch(2), enabled: true },
  { key: 'BENCH_ITERATIONS', value: ARGV.fetch(3, '10'), enabled: true },
  { key: 'BENCH_APP_PATH', value: ARGV.fetch(4, ''), enabled: true }
])
scheme.test_action.xml_element.attributes['shouldUseLaunchSchemeArgsEnv'] = 'NO'
scheme.save_as(project.path, 'PerformanceTests')
